using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UI.Behaviors
{
    /// <summary>
    /// 让 <see cref="DataGrid"/> 的多选结果（<c>SelectedItems</c>，只读集合）能双向绑定到 ViewModel。
    ///
    /// <para>为什么需要它：WPF 的 <c>DataGrid.SelectedItems</c> 是只读属性，既不是
    /// <c>DependencyProperty</c> 也不能直接赋 <c>Binding</c>；而"选中若干行 → 批量连接/断开"
    /// 这类操作要求 ViewModel 拿得到"当前选了哪几条"。项目里已有
    /// <see cref="TreeViewBehavior.BindableSelectedItemProperty"/> 解决 TreeView 的单选同步，
    /// 本类沿用同一套思路（附加属性 + 回环防护）补齐 DataGrid 的多选场景。</para>
    ///
    /// <para>回环防护说明：数据在两个方向流动——
    /// ① 界面选行 → 写回 VM 集合；② VM 集合变化 → 同步界面的选中行。
    /// 若不加标志，①会触发②、②又会触发①，形成死循环（表现为选中一行后界面持续闪烁）。
    /// 故用 <see cref="_syncingFromViewModel"/> 标记"当前正在由 VM 推给界面"，让①在此刻直接返回。</para>
    /// </summary>
    public static class DataGridSelectionBehavior
    {
        /// <summary>
        /// 多选集合。绑定目标必须是可写集合（<see cref="IList"/>，如
        /// <c>ObservableCollection&lt;T&gt;</c>）；只读集合无法承载界面的选中结果。
        ///
        /// <para>⚠️ 刻意<b>不</b>加 <c>BindsTwoWayByDefault</c>：本行为只把 DP 当"入口"读一次
        /// （拿到集合实例后就地增删），从不回写 DP —— 真正的双向是靠<b>集合实例本身</b>的
        /// <see cref="INotifyCollectionChanged"/> + <c>DataGrid.SelectionChanged</c> 完成的。
        /// 若声明为默认双向，<c>{Binding SelectedConfigs}</c> 会被隐式升成 TwoWay，
        /// WPF 在绑定期就会因"源属性只读"抛
        /// <c>InvalidOperationException: 无法对只读属性进行 TwoWay 绑定</c>，
        /// 被 XAML 包装成 <c>XamlParseException</c>，整张弹窗解析失败打不开
        /// （实机复现于 CommunicationSettingsView）。</para>
        /// </summary>
        public static readonly DependencyProperty BindableSelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "BindableSelectedItems",
                typeof(IList),
                typeof(DataGridSelectionBehavior),
                new FrameworkPropertyMetadata(
                    default(IList),
                    OnBindableSelectedItemsChanged));

        /// <summary>每个 DataGrid 对应一个集合变更处理器：换绑定目标时要能摘掉旧的（故必须记住委托实例）</summary>
        private static readonly DependencyProperty CollectionHandlerProperty =
            DependencyProperty.RegisterAttached(
                "CollectionHandler",
                typeof(NotifyCollectionChangedEventHandler),
                typeof(DataGridSelectionBehavior),
                new PropertyMetadata(null));

        /// <summary>该 DataGrid 是否已排好一次"把 VM 选中项推给界面"的任务（合并重复推送用）</summary>
        private static readonly DependencyProperty PushScheduledProperty =
            DependencyProperty.RegisterAttached(
                "PushScheduled",
                typeof(bool),
                typeof(DataGridSelectionBehavior),
                new PropertyMetadata(false));

        public static IList? GetBindableSelectedItems(DependencyObject obj) => (IList?)obj.GetValue(BindableSelectedItemsProperty);
        public static void SetBindableSelectedItems(DependencyObject obj, IList? value) => obj.SetValue(BindableSelectedItemsProperty, value);

        /// <summary>true = 正在由 VM 往 DataGrid 推送选中行，此时 DataGrid 自己冒出的选中变更不再写回 VM</summary>
        private static bool _syncingFromViewModel;

        #region 挂载 / 卸载

        private static void OnBindableSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            // 换绑定目标时先摘掉旧集合的监听，否则旧集合还在往一个已经没人看的 DataGrid 里推
            if (e.OldValue is INotifyCollectionChanged oldCollection
                && grid.GetValue(CollectionHandlerProperty) is NotifyCollectionChangedEventHandler oldHandler)
            {
                oldCollection.CollectionChanged -= oldHandler;
            }

            // 用闭包捕获 grid，从而在集合变化时能精确定位到"是哪个 DataGrid 要同步"，
            // 避免遍历整棵视觉树去找绑定目标
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                NotifyCollectionChangedEventHandler handler = (_, args) =>
                {
                    if (_syncingFromViewModel) return;
                    if (grid.GetValue(BindableSelectedItemsProperty) is not IList current) return;
                    // NotifyCollectionChangedEventArgs 上没有 OriginalSource（那是 WPF 路由事件的成员），
                    // 而且也不需要：这个 handler 是挂在 newCollection 上的，能收到 args 就说明来源是它，
                    // 直接拿 newCollection 与绑定目标比对即可。
                    if (!ReferenceEquals(newCollection, current)) return;
                    PushSelectionToGrid(grid, current);
                };
                newCollection.CollectionChanged += handler;
                grid.SetValue(CollectionHandlerProperty, handler);
            }
            else
            {
                grid.ClearValue(CollectionHandlerProperty);
            }

            // SelectionChanged 只在选中项真的变了才触发，比监听 MouseUp / Click 可靠
            // （键盘方向键、Ctrl+A、Shift 连选都能覆盖）。先摘再加，避免重复挂载时叠加
            grid.SelectionChanged -= OnDataGridSelectionChanged;
            grid.SelectionChanged += OnDataGridSelectionChanged;

            // 首次挂载：把 VM 里已有的选中项同步到界面（例如弹窗重开时恢复了上次的选择）
            if (e.NewValue is IList initial)
                PushSelectionToGrid(grid, initial);
        }

        #endregion

        #region 界面 → VM

        private static void OnDataGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingFromViewModel) return;
            if (sender is not DataGrid grid) return;

            var target = GetBindableSelectedItems(grid);
            if (target == null) return;

            _syncingFromViewModel = true;
            try
            {
                target.Clear();
                foreach (var item in grid.SelectedItems)
                    target.Add(item);
            }
            finally
            {
                _syncingFromViewModel = false;
            }
        }

        #endregion

        #region VM → 界面

        /// <summary>
        /// 把 VM 集合里的项逐条置为选中。
        /// 必须推迟到 <see cref="DispatcherPriority.Background"/>：容器生成要等布局完成，
        /// 在集合变更回调里直接走 <c>UpdateLayout</c> 属"布局过程中触发布局"，会抛异常。
        ///
        /// <para>合并重复推送：像"全选"这种往集合里连加 N 条的写法，每条都触发一次集合变更；
        /// 若每次都排一个任务，执行时又各自清空重加，就变成 O(N²) 的无用重排（200 条 = 4 万次操作）。
        /// 故用一个标志保证"同一时刻每个 DataGrid 只排一个任务"，任务执行时以当前集合内容为准。</para>
        /// </summary>
        private static void PushSelectionToGrid(DataGrid grid, IList source)
        {
            if ((bool)grid.GetValue(PushScheduledProperty)) return;
            grid.SetValue(PushScheduledProperty, true);

            grid.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    grid.SetValue(PushScheduledProperty, false);

                    _syncingFromViewModel = true;
                    try
                    {
                        grid.SelectedItems.Clear();
                        foreach (var item in source)
                        {
                            if (item == null) continue;
                            // 目标不在当前视图里（被搜索/状态筛选挡掉）会抛异常——静默跳过：
                            // 选中项不同步只是观感问题，抛异常会让整条绑定链断掉
                            try { grid.SelectedItems.Add(item); }
                            catch (InvalidOperationException) { }
                        }
                    }
                    finally
                    {
                        _syncingFromViewModel = false;
                    }
                }),
                DispatcherPriority.Background);
        }

        #endregion
    }
}
