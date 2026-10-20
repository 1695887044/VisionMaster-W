using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// GlobalVariableView.xaml 的交互逻辑。
    /// </summary>
    public partial class GlobalVariableView : UserControl
    {
        private GlobalVariableManagerViewModel? _vm;
        private GlobalVariableManagerViewModel? _hookedVm;

        public GlobalVariableView()
        {
            InitializeComponent();
            // 注意时序：Prism 的 AutoWireViewModel 附加属性在 InitializeComponent 解析期间
            // 就把 DataContext 设好了——早于下面这行订阅；DataContext 之后不再变化，
            // DataContextChanged 永远不会补发。所以订阅之外必须在构造器里直接补一次
            // HookViewModel()，否则列收放一次都不会执行
            //（2026-10-11 二次修复：首跑时"来源/扫描组/地址"三列没收起就是踩的这个坑）。
            DataContextChanged += (_, __) => HookViewModel();
            Loaded += (_, __) =>
            {
                HookViewModel();
                ClampToWorkArea();
            };
            Unloaded += (_, __) => UnhookViewModel();
            HookViewModel();
        }

        /// <summary>
        /// 视图绑定 VM 后的统一钩子（幂等）：
        /// ① 订阅 IsNetworkSource 变化 → 收放三列；
        /// ② 把左栏 ListBox 的 ItemsSource 设成按 IsNetwork 分组的默认视图。
        /// 分组视图只加一次 GroupDescription，避免重复 Hook 造成分组叠加/选中抖动。
        /// </summary>
        private void HookViewModel()
        {
            var next = DataContext as GlobalVariableManagerViewModel;
            if (!ReferenceEquals(_hookedVm, next))
            {
                UnhookViewModel();
                _hookedVm = next;
                _vm = next;
                if (_vm != null)
                    _vm.PropertyChanged += OnViewModelPropertyChanged;
            }

            _vm = next;
            if (_vm == null)
            {
                if (SourceList != null) SourceList.ItemsSource = null;
                return;
            }

            var view = CollectionViewSource.GetDefaultView(_vm.SourceNodes);
            if (view != null)
            {
                bool grouped = view.GroupDescriptions.Count == 1
                    && view.GroupDescriptions[0] is PropertyGroupDescription description
                    && string.Equals(description.PropertyName, nameof(VariableSourceNode.IsNetwork), System.StringComparison.Ordinal);

                if (!grouped)
                {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VariableSourceNode.IsNetwork)));
                }

                if (SourceList != null && !ReferenceEquals(SourceList.ItemsSource, view))
                    SourceList.ItemsSource = view;

                if (SourceList != null && !ReferenceEquals(SourceList.SelectedItem, _vm.SelectedSource))
                    SourceList.SelectedItem = _vm.SelectedSource;
            }

            ApplyDetailColumnMode();
        }

        private void UnhookViewModel()
        {
            if (_hookedVm != null)
                _hookedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _hookedVm = null;
            _vm = null;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GlobalVariableManagerViewModel.IsNetworkSource))
                ApplyDetailColumnMode();
        }

        /// <summary>
        /// 变量明细表的三列（来源/扫描组/地址）只对网络变量有意义：
        /// 本地/系统来源下整列收起，把宽度让给「变量名/当前值」。
        /// DataGridColumn 不在可视树，Visibility 不能用普通 Binding，只能由代码后置设置。
        /// </summary>
        private void ApplyDetailColumnMode()
        {
            var visibility = _vm?.IsNetworkSource == true ? Visibility.Visible : Visibility.Collapsed;
            if (SourceColumn != null) SourceColumn.Visibility = visibility;
            if (ScanGroupColumn != null) ScanGroupColumn.Visibility = visibility;
            if (AddressColumn != null) AddressColumn.Visibility = visibility;
        }

        /// <summary>
        /// 默认宽 1300 在小逻辑桌面（1920 屏 @150% → 工作区约 1280）会溢出，
        /// 打开时收敛到工作区；列显隐由 IsNetworkSource 自适应，窄屏下表格仍完整可用。
        /// </summary>
        private void ClampToWorkArea()
        {
            var window = Window.GetWindow(this);
            if (window == null)
            {
                return;
            }

            var workArea = SystemParameters.WorkArea;
            if (window.Width > workArea.Width)
            {
                window.Width = workArea.Width;
            }

            if (window.Height > workArea.Height)
            {
                window.Height = workArea.Height;
            }
        }
    }
}
