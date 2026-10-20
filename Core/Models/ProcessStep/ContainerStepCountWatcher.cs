using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VisionMaster.Models
{
    /// <summary>
    /// 容器「子步骤总数」的变更通知器。
    ///
    /// 为什么需要它：<c>TotalStepCount</c> 是只读计算属性（Children 各分支 Steps.Count 之和），
    /// WPF 绑定只在收到 PropertyChanged 时重读；ObservableCollection 只会替自己发 Count 通知，
    /// 不会替容器发。缺了这条通知，拖入/删除算子后流程栏上的「N 个算子」会一直停在旧值。
    ///
    /// 订阅面：容器的 Children 增删 + 每个分支 Steps 的增删。Children 被反序列化整体替换时
    /// （ObjectCreationHandling.Replace）由 Rebind 摘挂，避免旧集合继续引用。
    /// </summary>
    internal sealed class ContainerStepCountWatcher
    {
        private readonly Action _notify;
        private readonly Action<PropertyChangedEventArgs>? _notifyBranchChanged;
        private ObservableCollection<StepCollection>? _children;

        public ContainerStepCountWatcher(Action notify, Action<PropertyChangedEventArgs>? notifyBranchChanged = null)
        {
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
            _notifyBranchChanged = notifyBranchChanged;
        }

        /// <summary>把订阅面切到指定的 Children 集合（null 视为空集合）。重复传入同一实例是空操作。</summary>
        public void Rebind(ObservableCollection<StepCollection>? children)
        {
            if (ReferenceEquals(_children, children))
                return;

            if (_children != null)
            {
                _children.CollectionChanged -= OnChildrenChanged;
                foreach (var branch in _children)
                {
                    branch.Steps.CollectionChanged -= OnNestedChanged;
                    branch.PropertyChanged -= OnBranchPropertyChanged;
                }
            }

            _children = children;

            if (_children != null)
            {
                _children.CollectionChanged += OnChildrenChanged;
                foreach (var branch in _children)
                {
                    branch.Steps.CollectionChanged += OnNestedChanged;
                    branch.PropertyChanged += OnBranchPropertyChanged;
                }
            }
        }

        private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (StepCollection branch in e.OldItems)
                {
                    branch.Steps.CollectionChanged -= OnNestedChanged;
                    branch.PropertyChanged -= OnBranchPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (StepCollection branch in e.NewItems)
                {
                    branch.Steps.CollectionChanged += OnNestedChanged;
                    branch.PropertyChanged += OnBranchPropertyChanged;
                }
            }

            _notify();
        }

        private void OnNestedChanged(object? sender, NotifyCollectionChangedEventArgs e) => _notify();

        private void OnBranchPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => _notifyBranchChanged?.Invoke(e);
    }
}
