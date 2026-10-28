using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

    /// <summary>日志级别着色：ERROR 红 / WARN 橙 / 其余灰。颜色走公共令牌，取不到时退回命名色。</summary>
    public class LogLevelColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as string)
            {
                case "ERROR": return Resolve("DialogDangerTextBrush", Brushes.Firebrick);
                case "WARN": return Resolve("DialogWarningTextBrush", Brushes.DarkOrange);
                default: return Resolve("DialogTextMutedBrush", Brushes.Gray);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        internal static Brush Resolve(string key, Brush fallback)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }

    /// <summary>报文预览是否处于"无法预览"错误态（决定深色工作区里的文字颜色）。</summary>
    public class PreviewErrorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && s.StartsWith("（无法预览：", StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>预览头目标 URL：空串显示"未配置目标 URL"，不伪造地址。</summary>
    public class UrlDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? "未配置目标 URL" : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 连线桥接：把"字段名 + 宿主插件 + 端口重建计数"转换为该字段对应的动态输入端口，
    /// 让字段行内的 <c>LinkableValueEditor</c> 能按名挂到插件的动态端口上（R189）。
    /// 第三路 <c>PortsVersion</c> 只作重取锚点：端口对象每次重建都换新实例，
    /// 少了这一路 MultiBinding 不会重新求值，编辑器会一直指着已丢弃的旧端口。
    /// （与 Plugin.DataRecord / Plugin.CSharpScript 的 InputPortConverter 同一范式。）
    /// </summary>
    public class InputPortConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string name = values?.Length > 0 ? values[0] as string : null;
            var plugin = values?.Length > 1 ? values[1] as ResultUploadPlugin : null;
            return plugin?.GetInputPort(name);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
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
    /// 视图级命令：只把按钮点击转发到插件既有公开方法。
    /// 不引入独立 VM —— DataContext 仍是插件实例，宿主靠它取 IPluginConfigView.OnConfirm。
    /// </summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// 结果上报配置视图：报文预设 / 上报内容 / 发送策略 / 测试发送。
    /// 预览与字段联动的刷新由插件自身发 PropertyChanged，这里只管行增删、订阅与结果着色。
    /// </summary>
    public partial class ResultUploadView : UserControl
    {
        private ResultUploadPlugin Plugin => DataContext as ResultUploadPlugin;

        /// <summary>「添加字段」按钮：转发插件既有 AddField()。</summary>
        public ICommand AddFieldCommand { get; }

        /// <summary>「删除字段」按钮：转发 Fields.Remove(item)。</summary>
        public ICommand RemoveFieldCommand { get; }

        /// <summary>「添加请求头」按钮：转发插件既有 AddHeader()。</summary>
        public ICommand AddHeaderCommand { get; }

        /// <summary>「删除请求头」按钮：转发 Headers.Remove(item)。</summary>
        public ICommand RemoveHeaderCommand { get; }

        /// <summary>「立即发送」按钮：转发插件既有 TestSend()。</summary>
        public ICommand TestSendCommand { get; }

        /// <summary>「刷新日志」按钮：只重拉 RecentLogs 快照。</summary>
        public ICommand RefreshLogsCommand { get; }

        public ResultUploadView()
        {
            AddFieldCommand = new RelayCommand(_ => Plugin?.AddField());
            RemoveFieldCommand = new RelayCommand(
                parameter =>
                {
                    if (parameter is UploadFieldDef f)
                        Plugin?.Fields?.Remove(f);
                },
                parameter => parameter is UploadFieldDef);
            AddHeaderCommand = new RelayCommand(_ => Plugin?.AddHeader());
            RemoveHeaderCommand = new RelayCommand(
                parameter =>
                {
                    if (parameter is HttpHeaderDef h)
                        Plugin?.Headers?.Remove(h);
                },
                parameter => parameter is HttpHeaderDef);
            TestSendCommand = new RelayCommand(_ => Plugin?.TestSend());
            RefreshLogsCommand = new RelayCommand(_ => RefreshRecentLogs());

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

        /// <summary>
        /// 按 "✔" 前缀给结果文本着色：成功绿、失败红、未测试灰。
        /// 颜色全部取公共令牌；断言宿主没有 Application 资源时退回命名色。
        /// </summary>
        private void RefreshResultColor()
        {
            if (ResultText == null) return;
            string s = Plugin?.LastResult;
            if (s != null && s.StartsWith("✔"))
            {
                ResultText.Foreground = LogLevelColorConverter.Resolve("DialogSuccessTextBrush", Brushes.Green);
                ResultText.Background = LogLevelColorConverter.Resolve("DialogSuccessSubtleBrush", Brushes.Honeydew);
            }
            else if (!string.IsNullOrEmpty(s))
            {
                ResultText.Foreground = LogLevelColorConverter.Resolve("DialogDangerTextBrush", Brushes.Firebrick);
                ResultText.Background = LogLevelColorConverter.Resolve("DialogDangerSubtleBrush", Brushes.MistyRose);
            }
            else
            {
                ResultText.Foreground = LogLevelColorConverter.Resolve("DialogTextMutedBrush", Brushes.Gray);
                ResultText.Background = LogLevelColorConverter.Resolve("DialogRowAlternateBrush", Brushes.WhiteSmoke);
            }
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
    }
}
