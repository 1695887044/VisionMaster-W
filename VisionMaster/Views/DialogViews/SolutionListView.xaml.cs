using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using VisionMaster.Models;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 方案列表弹窗（软件级配置：方案清单 + 默认启动方案）
    /// </summary>
    public partial class SolutionListView : UserControl
    {
        public SolutionListView()
        {
            InitializeComponent();
        }

        private bool _hostWindowSized;

        /// <summary>
        /// Prism DialogWindow 默认按内容/宿主尺寸打开；本视图的表格是比例列，
        /// 不设默认尺寸时会被拉到整屏（用户实测 2000px 宽，名称列被挤成一条）。
        /// 首次 Loaded 时把宿主窗口钉到设计基线的 960×640（最小 900×560），
        /// 之后仍允许用户缩放，内容随窗口伸缩。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_hostWindowSized) return;
            _hostWindowSized = true;

            if (Window.GetWindow(this) is Window window)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Width = 960;
                window.Height = 640;
                window.MinWidth = 900;
                window.MinHeight = 560;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
        }

        /// <summary>
        /// 注释框里的 Enter 只提交本行，不冒泡到 IsDefault 的「确认」按钮。
        /// 绑定本身是 UpdateSourceTrigger=PropertyChanged，这里再显式 UpdateSource 一次，
        /// 保证"敲完最后一下直接点确认"时工作副本里一定是最新文本。
        /// </summary>
        private void CommentBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (sender is TextBox box)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }
    }

    /// <summary>
    /// 行内「默认」chip 的可见性：Path == StartupSolutionPath（不区分大小写）。
    /// 两个输入分别来自行的 Path 与 DataGrid 的 DataContext.StartupSolutionPath；
    /// 空路径一律不显示，避免"没设置默认"时所有空行都亮起。
    /// </summary>
    public sealed class PathEqualsToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return Visibility.Collapsed;
            if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                return Visibility.Collapsed;

            var path = values[0] as string;
            var startup = values[1] as string;
            return !string.IsNullOrEmpty(path)
                   && string.Equals(path, startup, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 卡片头的默认方案摘要：在 Solutions 里找 Path == StartupSolutionPath 的项，
    /// 显示它的 Name；找不到就退回文件名（例如清单刚被清空、路径还留着）。
    /// 纯显示派生，不新增 VM 属性。
    /// </summary>
    public sealed class StartupSummaryConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "未设置默认启动";
            if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                return "未设置默认启动";

            var startup = values[1] as string;
            if (string.IsNullOrEmpty(startup)) return "未设置默认启动";

            var entry = (values[0] as IEnumerable)?
                .OfType<AppSolutionEntry>()
                .FirstOrDefault(s => string.Equals(s.Path, startup, StringComparison.OrdinalIgnoreCase));

            var name = entry?.Name;
            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    name = Path.GetFileNameWithoutExtension(startup);
                }
                catch (ArgumentException)
                {
                    name = startup;
                }
            }

            return "默认：" + name;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// null / 空字符串 → Collapsed；ConverterParameter=Invert 时反过来。
    /// 用于「默认 chip 有/无」「底栏路径 有/无」「右栏提示 有/无选中」三处。
    /// </summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = value == null || (value is string s && s.Length == 0);
            bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
            bool visible = invert ? isEmpty : !isEmpty;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
