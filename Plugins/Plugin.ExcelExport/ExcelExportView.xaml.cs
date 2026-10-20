using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Plugin.ExcelExport
{
    /// <summary>导出方式枚举 → 中文标签（新手友好；未知值退回 ToString）。</summary>
    public class ExportPolicyLabelConverter : IValueConverter
    {
        private static readonly System.Collections.Generic.Dictionary<object, string> Labels =
            new System.Collections.Generic.Dictionary<object, string>
            {
                { ExportPolicy.Manual, "仅手动（点按钮才导）" },
                { ExportPolicy.OnTrigger, "触发端口（收到信号才导）" },
                { ExportPolicy.EachRun, "每次执行都导" },
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && Labels.TryGetValue(value, out var s) ? s : (value?.ToString() ?? "");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 枚举 → 可见性：ConverterParameter 写目标枚举名（如 "OnTrigger"）。
    /// 用于"选了某种导出方式才出现的行"（如触发端口那行的连线编辑器）。
    /// </summary>
    public class EnumEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal)
                ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>布尔取反（如 IsExporting=true 时按钮置灰，但 IsEnabled 需要的是"没在导出"）。</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value is bool b && b);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Excel 报表配置视图：来源账本 / 报表输出 / 报表选项 / 立即导出。
    /// 本视图的"立即导出"只是把配置跑一遍，不参与流程；正式流程由引擎调度。
    /// </summary>
    public partial class ExcelExportView : UserControl
    {
        private ExcelExportPlugin Plugin => DataContext as ExcelExportPlugin;

        /// <summary>
        /// 视图本地命令：只把按钮点击转发到插件既有的 <see cref="ExcelExportPlugin.ExportNow"/>，
        /// 不新增业务逻辑、不改插件契约。用命令而不是 Click，是为了让按钮入口可被统一冒烟断言检查。
        /// </summary>
        public ICommand ExportNowCommand { get; }

        public ExcelExportView()
        {
            ExportNowCommand = new RelayCommand(_ => Plugin?.ExportNow());
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 冒烟宿主/设计器只设置 DataContext、不进入 Loaded；这里先刷一次，
            // 让账本状态与结果面板在首次渲染时就有内容。真实窗口的 Loaded 会再刷一次。
            RefreshSourceExists();
            RefreshResultVisual();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin == null) return;
            // InstanceName（引擎注入）与 StepData.StepName 都是构造视图之后才齐的，故在此刷新一次预览
            plugin.RefreshPreview();
            plugin.PropertyChanged += OnPluginPropertyChanged;
            RefreshSourceExists();
            RefreshResultVisual();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin != null) plugin.PropertyChanged -= OnPluginPropertyChanged;
        }

        private void OnPluginPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // LastResult 由后台导出线程写入（Task.Run），事件回调也在后台线程；
            // 这里必须切回 UI 线程再碰控件，否则会抛跨线程访问异常。
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnPluginPropertyChanged(sender, e)));
                return;
            }

            if (e.PropertyName == nameof(ExcelExportPlugin.SourcePathPreview))
                RefreshSourceExists();
            else if (e.PropertyName == nameof(ExcelExportPlugin.LastResult))
                RefreshResultVisual();
            else if (e.PropertyName == nameof(ExcelExportPlugin.IsExporting))
                RefreshResultVisual();
        }

        /// <summary>即时校验"账本到底在不在"，把最容易配错的路径问题当场暴露出来。</summary>
        private void RefreshSourceExists()
        {
            if (SourceExistsText == null) return;
            string path = Plugin?.SourcePathPreview;
            if (string.IsNullOrWhiteSpace(path))
            {
                SourceExistsText.Text = "未配置账本路径";
                SourceExistsText.Foreground = FindBrush("DialogTextMutedBrush", Brushes.Gray);
                SourceExistsText.Visibility = Visibility.Visible;
                return;
            }

            bool exists = false;
            try { exists = File.Exists(path); } catch { /* 非法路径等一律当作不存在 */ }
            SourceExistsText.Text = exists
                ? "✔ 账本已找到"
                : "✘ 账本不存在（检查『源步骤名』是否与 CSV 记录步骤一致）";
            SourceExistsText.Foreground = exists
                ? FindBrush("DialogSuccessTextBrush", Brushes.Green)
                : FindBrush("DialogDangerTextBrush", Brushes.Firebrick);
            SourceExistsText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 结果面板四态：未导出（灰）/ 导出中（蓝）/ 成功（绿）/ 失败（红）。
        /// 只消费 LastResult 前缀与 IsExporting，不新增状态属性。
        /// </summary>
        private void RefreshResultVisual()
        {
            if (ResultText == null || ResultTitle == null) return;

            string s = Plugin?.LastResult ?? "";
            bool exporting = Plugin?.IsExporting == true;

            Brush muted = FindBrush("DialogTextMutedBrush", Brushes.Gray);
            Brush secondary = FindBrush("DialogTextSecondaryBrush", Brushes.DimGray);
            Brush accent = FindBrush("DialogAccentTextBrush", Brushes.SteelBlue);
            Brush success = FindBrush("DialogSuccessTextBrush", Brushes.Green);
            Brush danger = FindBrush("DialogDangerTextBrush", Brushes.Firebrick);

            if (s.StartsWith("✔", StringComparison.Ordinal))
            {
                SetResultState(
                    "导出成功",
                    success,
                    FindBrush("DialogSuccessSubtleBrush", Brushes.Honeydew),
                    FindBrush("DialogSuccessBorderBrush", Brushes.LightGreen),
                    "\uf00c");
                ShowResultText(true);
            }
            else if (s.StartsWith("✘", StringComparison.Ordinal))
            {
                SetResultState(
                    "导出失败",
                    danger,
                    FindBrush("DialogDangerSubtleBrush", Brushes.MistyRose),
                    FindBrush("DialogDangerBorderBrush", Brushes.LightCoral),
                    "\uf00d");
                ShowResultText(true);
            }
            else if (exporting || s.Length > 0)
            {
                SetResultState(
                    "导出进行中",
                    accent,
                    FindBrush("DialogAccentSoftBrush", Brushes.AliceBlue),
                    FindBrush("DialogAccentBorderBrush", Brushes.LightBlue),
                    "\uf110");
                ShowResultText(true);
            }
            else
            {
                ResultTitle.Text = "尚未导出";
                ResultTitle.Foreground = secondary;
                ResultText.Foreground = muted;
                if (ResultIcon != null)
                {
                    ResultIcon.Text = "\uf05a";
                    ResultIcon.Foreground = muted;
                }
                if (ResultPanel != null)
                {
                    ResultPanel.Background = FindBrush("DialogRowAlternateBrush", Brushes.WhiteSmoke);
                    ResultPanel.BorderBrush = FindBrush("DialogBorderLightBrush", Brushes.LightGray);
                }
                ShowResultText(false);
            }
        }

        private void SetResultState(string title, Brush foreground, Brush background, Brush border, string icon)
        {
            ResultTitle.Text = title;
            ResultTitle.Foreground = foreground;
            ResultText.Foreground = foreground;
            if (ResultIcon != null)
            {
                ResultIcon.Text = icon;
                ResultIcon.Foreground = foreground;
            }
            if (ResultPanel != null)
            {
                ResultPanel.Background = background;
                ResultPanel.BorderBrush = border;
            }
        }

        private void ShowResultText(bool show)
        {
            if (ResultText != null)
                ResultText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (ResultHint != null)
                ResultHint.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        private Brush FindBrush(string key, Brush fallback) =>
            TryFindResource(key) as Brush ?? fallback;

        /// <summary>最小 ICommand 实现：本视图只有"转发到插件方法"这一种用途。</summary>
        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;

            public RelayCommand(Action<object> execute) => _execute = execute;

            public event EventHandler CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _execute(parameter);
        }
    }
}
