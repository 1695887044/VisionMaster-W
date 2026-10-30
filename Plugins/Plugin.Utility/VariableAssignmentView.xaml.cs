using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Plugin.Util.Models;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 「变量定义与赋值」v4 配置视图。
    ///
    /// 视图本身不写业务逻辑：数据来自 DataContext（插件实例）与行视图模型（AssignmentRowVm）。
    ///
    /// 2026-10-30：行内「变量名」下拉已改为"整格可点 → 宿主变量选择弹窗"（AssignmentRowVm.PickNameCommand），
    /// 因此 <b>不再有 NameEditor ComboBox</b>，原先为它挂的六个"用户真的动了这一行"事件钩子一并删除——
    /// 那些钩子存在的前提是"行内有可编辑控件、程序灌值会与用户输入混在一起"（R35）；
    /// 名字只能经弹窗选择（一次明确的用户动作），前提消失了。
    /// </summary>
    public partial class VariableAssignmentView : UserControl
    {
        public VariableAssignmentView()
        {
            InitializeComponent();

            SizeChanged += OnViewSizeChanged;
        }

        private VariableAssignmentPlugin? Plugin => DataContext as VariableAssignmentPlugin;

        private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Plugin != null)
                Plugin.IsNarrow = ActualWidth < 700;
        }

        private void OnRowsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && sender is ListBox list)
                list.ScrollIntoView(e.AddedItems[0]);
        }
    }

    /// <summary>bool → Visibility 的反相版本（空态/未选中态用）。</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
