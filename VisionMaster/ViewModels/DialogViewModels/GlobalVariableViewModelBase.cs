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

        public ObservableCollection<VariableNode> DisplayNodes { get; } = new();
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
            UpdateFlatList();
        }

        /// <summary>
        /// 将树状结构压平为UI渲染用的一维列表
        /// </summary>
        protected virtual void UpdateFlatList()
        {
            DisplayNodes.Clear();
            foreach (var root in _realTree)
            {
                DisplayNodes.Add(root);
                if (root.IsExpanded)
                {
                    foreach (var child in root.Children)
                    {
                        DisplayNodes.Add(child);
                    }
                }
            }
        }

        /// <summary>
        /// 刷新整个树结构（保留展开状态）
        /// </summary>
        protected virtual void RefreshTree()
        {
            var expandedNames = _realTree.Where(n => n.IsExpanded).Select(n => n.Name).ToHashSet();
            _realTree.Clear();

            foreach (var gv in _workspace.GlobalVariables)
            {
                var rootNode = CreateRootNode(gv);
                rootNode.IsExpanded = expandedNames.Contains(rootNode.Name);

                if (ShouldCreateChildNodes(gv))
                {
                    CreateChildNodes(gv, rootNode);
                }

                _realTree.Add(rootNode);
            }

            UpdateFlatList();
        }

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

            VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => RefreshTree());
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
