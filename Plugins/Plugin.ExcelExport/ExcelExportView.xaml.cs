using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        public ExcelExportView()
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
            RefreshSourceExists();
            RefreshResultColor();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin != null) plugin.PropertyChanged -= OnPluginPropertyChanged;
        }

        private void OnPluginPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExcelExportPlugin.SourcePathPreview))
                RefreshSourceExists();
            else if (e.PropertyName == nameof(ExcelExportPlugin.LastResult))
                RefreshResultColor();
        }

        /// <summary>即时校验"账本到底在不在"，把最容易配错的路径问题当场暴露出来。</summary>
        private void RefreshSourceExists()
        {
            if (SourceExistsText == null) return;
            string path = Plugin?.SourcePathPreview;
            if (string.IsNullOrWhiteSpace(path))
            {
                SourceExistsText.Visibility = Visibility.Collapsed;
                return;
            }
            bool exists = false;
            try { exists = File.Exists(path); } catch { /* 非法路径等一律当作不存在 */ }
            SourceExistsText.Text = exists ? "✔ 账本已找到" : "✘ 账本不存在（检查『源步骤名』是否与 CSV 记录步骤一致）";
            SourceExistsText.Foreground = exists
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            SourceExistsText.Visibility = Visibility.Visible;
        }

        private void RefreshResultColor()
        {
            if (ResultText == null) return;
            string s = Plugin?.LastResult;
            ResultText.Foreground = s != null && s.StartsWith("✔")
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        }

        private void ExportNow_Click(object sender, RoutedEventArgs e)
        {
            Plugin?.ExportNow();
        }
    }
}
