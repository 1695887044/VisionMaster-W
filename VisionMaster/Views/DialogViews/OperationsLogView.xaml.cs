using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 操作记录弹窗（只读）。查询 / 分页 / 权限 / 复制 / 导出都在视图模型里；
    /// 这里只做两件视图级的事：双击行的落点守卫、打开后把焦点/选中落到第一行（设计稿 §6）。
    /// </summary>
    public partial class OperationsLogView : UserControl
    {
        private bool _firstRowFocusDone;
        private OperationsLogViewModel? _observed;

        public OperationsLogView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// 双击行 = 复制该行明细（设计稿 §6）。写剪贴板与提示都在视图模型里；
        /// 这里只判"点有没有落在行上"——双击空白/表头不触发。
        /// </summary>
        private void RowsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not OperationsLogViewModel vm) return;
            if (e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement((ItemsControl)sender, source) == null) return;

            vm.CopySelectedRowDetail();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OperationsLogViewModel vm) return;

            if (!ReferenceEquals(_observed, vm))
            {
                if (_observed != null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
                _observed = vm;
                _observed.PropertyChanged += OnViewModelPropertyChanged;
            }

            TryFocusFirstRow(vm);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_observed != null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
            _observed = null;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 首屏是异步查询：Rows 到位的那一刻才谈得上"第一行"。
            if (sender is OperationsLogViewModel vm
                && (e.PropertyName == nameof(OperationsLogViewModel.IsLoading)
                    || e.PropertyName == nameof(OperationsLogViewModel.HasRows)))
                TryFocusFirstRow(vm);
        }

        /// <summary>设计稿 §6：打开弹窗后列表焦点在第一行（每个弹窗实例只做一次）。</summary>
        private void TryFocusFirstRow(OperationsLogViewModel vm)
        {
            if (_firstRowFocusDone || vm.IsLoading || vm.Rows.Count == 0) return;

            _firstRowFocusDone = true;
            if (vm.SelectedRow == null) vm.SelectedRow = vm.Rows[0];
            RowsList.Focus();
            if (vm.SelectedRow != null) RowsList.ScrollIntoView(vm.SelectedRow);
        }
    }
}
