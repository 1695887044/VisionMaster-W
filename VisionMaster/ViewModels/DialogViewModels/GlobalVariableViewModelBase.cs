using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 全局变量视图模型基类（封装所有公共树操作逻辑）
    /// </summary>
    public abstract class GlobalVariableViewModelBase : BindableBase, IDisposable
    {
        protected readonly IWorkspaceManager _workspace;
        protected List<VariableNode> _realTree = new();
        private bool _disposed = false;

        public DelegateCommand<VariableNode> ToggleNodeCommand { get; }

        protected GlobalVariableViewModelBase(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            ToggleNodeCommand = new DelegateCommand<VariableNode>(ToggleNode);
            _workspace.GlobalVariables.CollectionChanged += OnGlobalVariablesCollectionChanged;

            // 存量变量挂接监听并纳入跟踪集（子类 OnVariableAdded 只做事件订阅，不触字段，安全）
            foreach (var gv in _workspace.GlobalVariables)
            {
                if (_trackedVariables.Add(gv))
                    OnVariableAdded(gv);
            }
        }

        /// <summary>
        /// 切换节点展开/折叠状态
        /// </summary>
        protected virtual void ToggleNode(VariableNode node)
        {
            if (node == null || !node.IsContainer) return;
            node.IsExpanded = !node.IsExpanded;
            RefreshDisplay();
        }

        /// <summary>
        /// 把 <see cref="_realTree"/> 重新铺到界面上（子类实现）。
        ///
        /// 基类不再自备一份扁平表：明细表是"整棵树 → 来源/分组/搜索过滤 → 扁平行"一次算完的
        /// （见 <c>GlobalVariableManagerViewModel.UpdateFilteredList</c>），
        /// 中间再存一份只写不读的 <c>DisplayNodes</c> 只会误导下一个人去绑它。
        /// </summary>
        protected virtual void RefreshDisplay()
        {
        }

        /// <summary>
        /// 树建好后的后处理钩子（子类可在此按名称路径归组等）。
        /// 默认原样返回。
        /// </summary>
        protected virtual List<VariableNode> PostProcessTree(List<VariableNode> roots) => roots;

        /// <summary>
        /// 刷新整个树结构（保留展开状态）
        /// </summary>
        protected virtual void RefreshTree()
        {
            // 展开态按稳定身份记忆：按名字记的话，变量一改名新树里的节点
            // 与旧键对不上，展开中的数组变量会莫名其妙自动折叠。
            // 旧工程数据（Id 为空）退回按名记，行为与从前一致；
            // 合成容器（按名称路径归组出来的）用路径键，见 VariableNode.ExpandKey。
            var expandedKeys = CollectExpandedKeys(_realTree);

            _realTree.Clear();

            foreach (var gv in EnumerateTreeVariables())
            {
                var rootNode = CreateRootNode(gv);

                if (ShouldCreateChildNodes(gv))
                {
                    CreateChildNodes(gv, rootNode);
                }

                _realTree.Add(rootNode);
            }

            _realTree = PostProcessTree(_realTree);
            RestoreExpandedKeys(_realTree, expandedKeys);
            RefreshDisplay();
        }

        /// <summary>
        /// 收集"需要记忆展开态"的节点键：合成容器（路径键）与顶层真实变量。
        /// 数组元素行不记忆——它们随父数组长度变化而重建，记忆反而会留下错位状态。
        /// </summary>
        private static HashSet<string> CollectExpandedKeys(IEnumerable<VariableNode> roots)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            void Walk(VariableNode node)
            {
                if (node.IsExpanded)
                {
                    var key = ExpansionKeyOf(node);
                    if (!string.IsNullOrEmpty(key)) keys.Add(key!);
                }
                foreach (var child in node.Children) Walk(child);
            }

            foreach (var root in roots) Walk(root);
            return keys;
        }

        private static void RestoreExpandedKeys(IEnumerable<VariableNode> roots, HashSet<string> keys)
        {
            if (keys.Count == 0) return;

            void Walk(VariableNode node)
            {
                var key = ExpansionKeyOf(node);
                if (!string.IsNullOrEmpty(key) && keys.Contains(key!))
                    node.IsExpanded = true;
                foreach (var child in node.Children) Walk(child);
            }

            foreach (var root in roots) Walk(root);
        }

        /// <summary>
        /// 节点的展开态记忆键。合成容器用 <see cref="VariableNode.ExpandKey"/>（名称路径），
        /// 顶层真实变量用 VariableId（旧数据回落名称），数组元素行返回 null（不记忆）。
        /// </summary>
        private static string? ExpansionKeyOf(VariableNode node)
        {
            if (!string.IsNullOrEmpty(node.ExpandKey)) return node.ExpandKey;
            if (!node.IsRootNode || node.OriginalModel == null) return null;

            return node.OriginalModel.VariableId != Guid.Empty
                ? "id:" + node.OriginalModel.VariableId
                : "name:" + node.Name;
        }

        /// <summary>
        /// 明细树的数据源。
        ///
        /// 默认只有用户变量（<see cref="IWorkspaceManager.GlobalVariables"/>）。
        /// 变量管理弹窗还要把「系统变量」分区显示出来，而系统/IO 变量自 2026-10-18 起
        /// 住在 <see cref="IWorkspaceManager.SystemVariables"/>（不落盘、不进 GlobalVariables），
        /// 所以由子类覆写把两边合并；本基类的其它消费方（无）保持原语义。
        /// </summary>
        protected virtual IEnumerable<IVariable> EnumerateTreeVariables() => _workspace.GlobalVariables;

        #region 子类重写方法
        /// <summary>
        /// 创建根节点（子类必须实现）
        /// </summary>
        protected abstract VariableNode CreateRootNode(IVariable gv);

        /// <summary>
        /// 判断是否需要为当前变量创建子节点
        /// </summary>
        protected virtual bool ShouldCreateChildNodes(IVariable gv)
        {
            return gv.DataType?.IsArray == true;
        }

        /// <summary>
        /// 创建子节点（子类必须实现）
        /// </summary>
        protected abstract void CreateChildNodes(IVariable gv, VariableNode parentNode);
        #endregion

        /// <summary>
        /// 全局变量集合变化事件处理：
        /// 增删时回调 OnVariableAdded/OnVariableRemoved（供子类挂接变量级监听），再刷新树。
        /// B2：用 _trackedVariables 跟踪已挂接的变量——Clear() 触发的是 Reset 动作且 OldItems 为 null，
        /// 旧实现拿不到旧清单，被清掉的变量永远挂着弹窗订阅（切一次方案漏一轮弹窗+幽灵通知）
        /// </summary>
        private readonly HashSet<IVariable> _trackedVariables = new();

        /// <summary>子类 Dispose 退订时用：本 VM 实际挂过监听的变量全集（含已被 Clear 掉的）</summary>
        protected IReadOnlyCollection<IVariable> TrackedVariables => _trackedVariables;

        protected virtual void OnGlobalVariablesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                        foreach (IVariable v in e.NewItems)
                            if (_trackedVariables.Add(v)) OnVariableAdded(v);
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                        foreach (IVariable v in e.OldItems)
                            if (_trackedVariables.Remove(v)) OnVariableRemoved(v);
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null)
                        foreach (IVariable v in e.OldItems)
                            if (_trackedVariables.Remove(v)) OnVariableRemoved(v);
                    if (e.NewItems != null)
                        foreach (IVariable v in e.NewItems)
                            if (_trackedVariables.Add(v)) OnVariableAdded(v);
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    // Clear()：集合已清空，靠跟踪集补退订（方案切换 VariablePersistenceService.Restore 走的就是这里）
                    foreach (var v in _trackedVariables)
                        OnVariableRemoved(v);
                    _trackedVariables.Clear();
                    break;
            }

            QueueTreeRefresh();
        }

        private bool _refreshPosted;

        /// <summary>
        /// 合并触发的树重建：批量导入 2357 个变量时**每条 Add** 都会走到这里，
        /// 不合并就是 2357 次 O(N) 全量重建（O(N²)，实测 UI 线程卡一分钟——
        /// 2026-10-29 汇川 2357 点导入实测）。同一轮调度里只排一次重建，
        /// 排队期间集合再怎么变，最终那次 RefreshTree 拿到的都是最终状态。
        /// 单条增删的行为不变（一次 post 一次重建）。
        /// </summary>
        protected void QueueTreeRefresh()
        {
            if (_refreshPosted) return;
            _refreshPosted = true;
            VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
            {
                _refreshPosted = false;
                RefreshTree();
            });
        }

        /// <summary>变量加入集合时回调（子类在此补挂变量级事件监听）</summary>
        protected virtual void OnVariableAdded(IVariable variable) { }

        /// <summary>变量移出集合时回调（子类在此退订变量级事件监听）</summary>
        protected virtual void OnVariableRemoved(IVariable variable) { }

        #region IDisposable实现
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _workspace.GlobalVariables.CollectionChanged -= OnGlobalVariablesCollectionChanged;
            }

            _disposed = true;
        }

        // B5 修正：原代码写成了"无 ~ 的私有构造函数"——永远无人调用（本类构造都走带参重载），
        // 属误导性死代码；还原为真正的终结器兜底（Dispose(false) 只置标志、不触碰托管对象，符合终结器纪律）
        ~GlobalVariableViewModelBase()
        {
            Dispose(false);
        }
        #endregion
    }
}
