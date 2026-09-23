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

    /// <summary>日志级别着色：ERROR 红 / WARN 橙 / 其余灰。</summary>
    public class LogLevelColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as string)
            {
                case "ERROR": return new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                case "WARN": return new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
                default: return new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// PasswordBox 的双向绑定桥：PasswordBox 出于安全设计不暴露 Password 依赖属性（XAML 直接 Binding 会被拒），
    /// 用附加属性中转。对应"只打码不加密"的拍板——持久化仍是明文，只是界面上打码显示。
    /// </summary>
    public static class PasswordBoxHelper
    {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
                new PropertyMetadata("", OnBoundPasswordChanged));

        public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PasswordBox box)
            {
                box.PasswordChanged -= Box_PasswordChanged;
                if (!string.Equals(box.Password, (string)e.NewValue))
                    box.Password = (string)e.NewValue ?? "";
                box.PasswordChanged += Box_PasswordChanged;
            }
        }

        private static void Box_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox box)
                SetBoundPassword(box, box.Password);
        }
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
            RefreshRecentLogs();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin != null) plugin.PropertyChanged -= OnPluginPropertyChanged;
        }

        private void OnPluginPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResultUploadPlugin.LastResult))
            {
                // 异步模式：LastResult 在队列线程被赋值，本事件处理器跟着跑在后台线程——
                // 摸 UI（Foreground / ItemsSource）必须封送回 UI 线程，否则跨线程异常
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshResultColor();
                    RefreshRecentLogs();
                }));
            }
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

        /// <summary>拉一次最近日志快照（最多 50 条）填进回看列表。</summary>
        private void RefreshRecentLogs()
        {
            if (RecentLogsList == null) return;
            var logs = Plugin?.RecentLogs;
            if (logs == null) return;
            RecentLogsList.ItemsSource = logs;
            if (RecentLogsList.Items.Count > 0)
                RecentLogsList.ScrollIntoView(RecentLogsList.Items[RecentLogsList.Items.Count - 1]);
        }

        private void RefreshLogs_Click(object sender, RoutedEventArgs e) => RefreshRecentLogs();

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
