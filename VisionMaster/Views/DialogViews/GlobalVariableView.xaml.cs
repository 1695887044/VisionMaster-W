using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// GlobalVariableView.xaml 的交互逻辑。
    /// </summary>
    public partial class GlobalVariableView : UserControl
    {
        private GlobalVariableManagerViewModel? _vm;

        public GlobalVariableView()
        {
            InitializeComponent();
            // 注意时序：Prism 的 AutoWireViewModel 附加属性在 InitializeComponent 解析期间
            // 就把 DataContext 设好了——早于下面这行订阅；DataContext 之后不再变化，
            // DataContextChanged 永远不会补发。所以订阅之外必须在构造器里直接补一次
            // HookViewModel()，否则列收放一次都不会执行
            //（2026-10-11 二次修复：首跑时"来源/扫描组/地址"三列没收起就是踩的这个坑）。
            DataContextChanged += (_, __) => HookViewModel();
            Loaded += (_, __) => ClampToWorkArea();
            HookViewModel();
        }

        /// <summary>
        /// 变量明细表的三列（来源/扫描组/地址）只对网络变量有意义：
        /// 本地来源下每行恒为「本地」徽标或空白，整列收起把宽度让给
        /// 「变量名/初始值/当前值」——否则默认窗口宽下固定列合计超过视口，
        /// 星号"当前值"列会被压成 0 宽（2026-10-11"变量明细显示不全"事故）。
        /// 切换信号用 IsNetworkSource：SelectedSource 的 setter（本地节点与
        /// 连接节点互斥切换）与本地/网络胶囊两条路径都会推进它。
        /// </summary>
        private void HookViewModel()
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _vm = DataContext as GlobalVariableManagerViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnViewModelPropertyChanged;
                ApplyDetailColumnMode();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GlobalVariableManagerViewModel.IsNetworkSource))
            {
                ApplyDetailColumnMode();
            }
        }

        private void ApplyDetailColumnMode()
        {
            var visibility = _vm?.IsNetworkSource == true ? Visibility.Visible : Visibility.Collapsed;
            SourceColumn.Visibility = visibility;
            ScanGroupColumn.Visibility = visibility;
            AddressColumn.Visibility = visibility;
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
