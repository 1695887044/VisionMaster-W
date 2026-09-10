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
        /// 增删时回调 OnVariableAdded/OnVariableRemoved（供子类挂接变量级监听），再刷新树
        /// </summary>
        protected virtual void OnGlobalVariablesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                        foreach (IVariable v in e.NewItems) OnVariableAdded(v);
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                        foreach (IVariable v in e.OldItems) OnVariableRemoved(v);
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null)
                        foreach (IVariable v in e.OldItems) OnVariableRemoved(v);
                    if (e.NewItems != null)
                        foreach (IVariable v in e.NewItems) OnVariableAdded(v);
                    break;
            }

            Application.Current.Dispatcher.Invoke(() => RefreshTree());
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

        GlobalVariableViewModelBase()
        {
            Dispose(false);
        }
        #endregion
    }
}
