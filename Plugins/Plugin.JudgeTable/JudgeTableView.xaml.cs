using Plugin.JudgeTable.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Plugin.JudgeTable
{
    /// <summary>
    /// 规则条数为 0 时显示空态提示（纯展示转换；数据源是 <c>Rules.Count</c>，ObservableCollection 会通知它）。
    /// </summary>
    public sealed class ZeroToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 连线桥接：把「本行输入端口名 + 宿主插件 + 端口重建计数」转换成当前输入端口对象，
    /// 让行内 <c>LinkableValueEditor</c> 按名挂到插件的动态端口上（R189 的强制范式）。
    ///
    /// 第三路 <c>PortsVersion</c> 只作重取锚点：端口对象每次重建都换新实例，
    /// 少了这一路 MultiBinding 不会重新求值，编辑器会一直指着已丢弃的旧端口
    /// （表现为"手填常量不落盘、🔗 把连线写到旧端口名"）。
    /// （与 Plugin.DataRecord / Plugin.ResultUpload 的 InputPortConverter 同一范式。）
    /// </summary>
    public sealed class InputPortConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string? portName = values?.Length > 0 ? values[0] as string : null;
            var plugin = values?.Length > 1 ? values[1] as JudgeTablePlugin : null;
            return plugin?.GetInputPort(portName);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 判定表配置视图——纯绑定层（MVVM）：判定、端口重建、运行时变量写入全在
    /// <see cref="JudgeTablePlugin"/> 里，本文件只剩两件事：
    /// ① 视图就绪信号 → 插件重建端口（<see cref="JudgeTablePlugin.OnViewLoaded"/>）；
    /// ② 双击行展开/收起（设计稿「点行展开」；点在按钮/输入框/下拉上时不抢事件）。
    /// </summary>
    public partial class JudgeTableView : UserControl
    {
        public JudgeTableView()
        {
            InitializeComponent();
            Loaded += (_, _) => (DataContext as JudgeTablePlugin)?.OnViewLoaded();
        }

        /// <summary>
        /// 双击行展开 / 收起。点在这几类控件上时不处理，交还给控件自己
        /// （否则双击改名框、双击开关都会顺手把行折叠掉）。
        /// </summary>
        private void RulesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;

            if (FindAncestor<ButtonBase>(source) != null) return;   // 按钮 / 开关 / 复选框（ComboBox 内部也是 ToggleButton）
            if (FindAncestor<TextBoxBase>(source) != null) return;  // 正在改名 / 填期望
            if (FindAncestor<ComboBox>(source) != null) return;     // 模式 / 集合语义下拉

            if (FindAncestor<ListViewItem>(source)?.DataContext is JudgeRule rule)
            {
                rule.IsExpanded = !rule.IsExpanded;
                e.Handled = true;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T match) return match;
                node = node is Visual or Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }
    }
}
