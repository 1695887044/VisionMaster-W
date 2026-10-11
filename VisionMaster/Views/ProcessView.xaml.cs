using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VisionMaster.Models;

namespace VisionMaster.Views
{
    /// <summary>
    /// ProcessView.xaml 的交互逻辑
    /// </summary>
    public partial class ProcessView : UserControl
    {
        public ProcessView()
        {
            InitializeComponent();

            // 入树/离树成对挂摘订阅：AvalonDock 切换标签页、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
            DataContextChanged += OnViewDataContextChanged;

            StepTree.PreviewMouseLeftButtonDown += OnTreePreviewMouseLeftButtonDown;
        }

        // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
        // 此时入树事件已经过去，需要在这里补挂一次（Activate 幂等，不会重复订阅）
        private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                (e.NewValue as ViewModels.ProcessViewModel)?.Activate();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => (DataContext as ViewModels.ProcessViewModel)?.Activate();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => (DataContext as ViewModels.ProcessViewModel)?.Deactivate();
        private void moduleTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 分支卡片没有算子级动作（它自己的条件在容器上配、名字由容器面板改），
            // 出一份"按当前选中项取参"的菜单只会让命令打在旧算子上（删除/禁用/参数全是错靶）。
            //
            // 2026-10-09 新开的唯一口子：并行分组的分支胶囊自带「重命名 / 删除本条分支」两项菜单
            // （结构动作从参数面板下沉到流程栏）。命中胶囊时**不设 Handled**，让 ContextMenuService
            // 打开"最近的那份"菜单（= 胶囊自带那份，命令靶=命中分支）；两条路都不 Focus、不改选中，
            // 分支卡片依旧不进"当前选中项"（R25）。If/For/While 的分支与分支行的空白背景维持原拦截——
            // 放行只会打开父级 TreeView 菜单（按当前选中项取参，正是上面说的错靶）。
            if (IsBranchCardUnderMouse(e))
            {
                if (!IsParallelBranchCapsuleUnderMouse(e))
                    e.Handled = true;
                return;
            }

            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(StepTree);
            HitTestResult result = VisualTreeHelper.HitTest(StepTree, pt);
            if (result == null)
                return;
            TreeViewItem selectedItem = FindVisualParent<TreeViewItem>(
                result.VisualHit
            );

            if (selectedItem != null)
            {
                selectedItem.Focus();
            }
            else
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 命中的分支卡片是不是"并行分组的分支胶囊"本身（而不是分支行的空白背景）。两个条件缺一不可：
        ///  ① 命中元素的祖先链里挂着一份 ContextMenu —— 那就是胶囊 Border 自带的分支级菜单；
        ///     只看②不看①的话，右键分支行的空白背景会放行 → 打开父级 TreeView 菜单（错靶菜单）；
        ///  ② 所属容器项的数据项是 ParallelStep —— 分支的父级 TreeViewItem 由
        ///     ItemsControlFromItemContainer 给出（分支项自己是谁的容器项，一目了然）。
        /// </summary>
        private bool IsParallelBranchCapsuleUnderMouse(MouseButtonEventArgs e)
        {
            var source = (e.OriginalSource ?? e.Source) as DependencyObject;
            if (source == null)
                return false;

            var item = FindVisualParent<TreeViewItem>(source);
            if (item?.DataContext is not StepCollection)
                return false;
            if (ItemsControl.ItemsControlFromItemContainer(item)?.DataContext is not ParallelStep)
                return false;

            // 到分支项为止找菜单属主：再往上就是 TreeView 那份父级菜单了，不算数
            for (var d = source; d != null && d != item; d = VisualTreeHelper.GetParent(d))
            {
                if (d is FrameworkElement element && element.ContextMenu != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 双击分支卡片弹出的"上一个算子"的参数窗/空条件窗（2026-10-09 真机）：
        /// 双击链是"TreeViewItem 选中 → BindableSelectedItem 写回 VM → 命令按选中项取参"，
        /// 而 VM 的 SelectStep 明确拒绝 StepCollection，于是写回被吞、命令打在旧选中项上。
        /// 在 Preview（隧道阶段）拦掉左键：TreeViewItem 的选中与其后的 MouseDoubleClick 派发都不再发生
        /// （WPF 里阻止 TreeViewItem 选中的标准做法）。悬停高亮与"拖入分支"不受影响（拖放走 OLE）。
        /// </summary>
        private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsBranchCardUnderMouse(e))
                e.Handled = true;
        }

        /// <summary>
        /// 鼠标命中的 TreeViewItem 是不是"分支卡片"（数据项为 StepCollection）。
        /// 两条豁免先放行：
        ///  · **展开/折叠箭头**（ToggleButton）——它就在分支卡片自己的容器模板里，按"命中的 TreeViewItem
        ///    是不是 StepCollection"判会连它一起吃掉，后果是鼠标点不开分支、分支内算子在流程栏里不可见
        ///    （2026-10-09 审查抓到的回归；TreeViewBehavior 对双击箭头也有同款豁免）；
        ///  · 命中元素取不到（真实鼠标输入必有 OriginalSource，手工 RaiseEvent 回放只有 Source，故二者取一）。
        /// </summary>
        private bool IsBranchCardUnderMouse(MouseButtonEventArgs e)
        {
            var source = (e.OriginalSource ?? e.Source) as DependencyObject;
            if (source == null)
                return false;
            for (var d = source; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ToggleButton)
                    return false;
            }
            return FindVisualParent<TreeViewItem>(source)?.DataContext is StepCollection;
        }

        /// <summary>
        /// 沿可视树/逻辑树向上找指定类型的祖先。
        /// 双路回退照 TreeViewBehavior.FindAncestor：OriginalSource 可能是 ContentElement（Run/TextBlock
        /// 里的内联文本等非 Visual），此时 VisualTreeHelper.GetParent 会抛 InvalidOperationException，
        /// 而异常从预览鼠标处理器里冒出去会中断整条输入处理。
        /// </summary>
        public T FindVisualParent<T>(DependencyObject obj) where T : class
        {
            while (obj != null)
            {
                if (obj is T)
                    return obj as T;

                obj =
                    (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
                        ? VisualTreeHelper.GetParent(obj)
                        : LogicalTreeHelper.GetParent(obj);
            }

            return null;
        }
    }
}
