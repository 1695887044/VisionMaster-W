using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UI.Attributes;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    /// <summary>
    /// 属性网格的**公共基类**：依赖属性、生成/处理管线、事件清理、重绘防抖、跨线程兜底。
    ///
    /// 【为什么抽它】改造前 FlatPropertyGrid(260 行) 与 CardPropertyGrid(327 行) 是两份几乎
    /// 逐字相同的实现：同样的三个防爆字段、同样的 BindingObject 依赖属性、同样的
    /// 属性变更监听与重绘防抖，只差"内容怎么摆"。重复代码最贵的代价不是行数，
    /// 而是**修一处漏一处**：下面这些坑曾经只在一边修过。
    ///
    /// 【本次抽出时顺带修掉的分歧】
    ///   · 生成器命中顺序：Card 原来是 OrderBy（升序），会把 Priority=0 的兜底
    ///     TypeGenerator 当成**最优先**，多数属性因此走了兜底文本控件；统一为降序
    ///     （Priority 高者优先，兜底最后）—— Priority 的语义本来就是"越大越优先"。
    ///   · 线程兜底：只有 Flat 做了 Dispatcher.CheckAccess 封送，Card 没有，
    ///     后台线程改属性时 Card 会 VerifyAccess 抛异常。统一在基类做。
    ///
    /// 子类只需实现：DefaultStyleKey、<see cref="UseCardLayout"/>、<see cref="BuildTabContent"/>。
    /// </summary>
    public abstract class PropertyGridBase : Control
    {
        protected const string DefaultName = PropertyGridDefaults.DefaultGroupName;

        /// <summary>本实例专属的共享尺寸组名，避免不同属性网格之间标签列宽互相牵制</summary>
        private readonly string _sharedLabelGroup = "PropertyGridLabel_" + Guid.NewGuid().ToString("N");

        /// <summary>暴露给布局处理器使用的实例专属 SharedSizeGroup 名</summary>
        protected string SharedLabelGroup => _sharedLabelGroup;

        /// <summary>控件生成器（可外部追加；默认清单见 PropertyGridDefaults）</summary>
        public List<IControlGenerator> Generators { get; } = PropertyGridDefaults.CreateGenerators();

        /// <summary>额外处理器（在默认管线之后执行）</summary>
        public List<IControlProcessor> Processors { get; } = new();

        #region 防爆字段：僵尸事件 / 监听泄漏 / 重绘风暴

        /// <summary>卸载委托链：每次重绘前执行并清空，防止旧控件上的事件泄漏</summary>
        private Action _cleanupActions = () => { };

        /// <summary>当前数据源的 INPC 监听</summary>
        private INotifyPropertyChanged? _currentNotifier;

        /// <summary>重绘防抖标志</summary>
        private bool _isRefreshPending;

        /// <summary>懒加载：已构建内容的 Tab 集合，避免重复构建</summary>
        private readonly HashSet<TabItem> _builtTabs = new();

        /// <summary>分组键 → TabItem 映射，供 RequireRefresh 精准重建单个 Tab</summary>
        private Dictionary<string, TabItem> _tabByKey = new();

        /// <summary>
        /// 已挂上 SelectionChanged 的那个 TabControl 实例（挂在"实例"上，而不是用一个布尔标志）。
        /// 用布尔标志会有个静默缺陷：模板一旦被换过（换主题/换样式 → 新的 TabControl 实例），
        /// 标志仍为 true，事件就永远挂不到新实例上 —— 表现是**除当前页签外，点别的页签永远空白**，
        /// 而且不报错（懒加载回调没人触发）。所以这里记实例、按实例比对。
        /// </summary>
        private TabControl? _hookedTabControl;

        #endregion

        public static readonly DependencyProperty BindingObjectProperty =
            DependencyProperty.Register(
                nameof(BindingObject),
                typeof(object),
                typeof(PropertyGridBase),
                new PropertyMetadata(null, OnBindingObjectChanged));

        public object BindingObject
        {
            get => GetValue(BindingObjectProperty);
            set => SetValue(BindingObjectProperty, value);
        }

        /// <summary>
        /// 高度无限时的自钳制上限（依赖属性，宿主可覆盖）。
        /// 替代原来的"去量 MainWindow.ActualHeight - 250"——控件不该感知宿主窗口。
        /// 设为 0 表示不钳制（完全交给外层容器）。
        /// </summary>
        public static readonly DependencyProperty MaxAutoHeightProperty =
            DependencyProperty.Register(
                nameof(MaxAutoHeight),
                typeof(double),
                typeof(PropertyGridBase),
                new PropertyMetadata(640d));

        public double MaxAutoHeight
        {
            get => (double)GetValue(MaxAutoHeightProperty);
            set => SetValue(MaxAutoHeightProperty, value);
        }

        /// <summary>true = 卡片式布局（标签右对齐 + 12 栅格）；false = 扁平表格式布局</summary>
        protected abstract bool UseCardLayout { get; }

        /// <summary>程序集内可见的布局形态判断，供嵌套生成器等同程序集组件读取</summary>
        internal bool IsCardLayout => UseCardLayout;

        /// <summary>构建一个 Tab 的内容（两种面板的容器不同，交给子类）。懒加载：仅在该 Tab 被选中时调用</summary>
        protected abstract void BuildTabContent(TabItem tabItem, IGrouping<string, PropertyInfo> group);

        /// <summary>按一级分组聚合属性（顺序同原逻辑）。
        /// GroupOrder 虽是 string，但语义是数字档位：必须按数值比较，否则 "10" 会排在 "2" 前面。</summary>
        private static List<IGrouping<string, PropertyInfo>> GroupProperties(List<PropertyInfo> properties) =>
            properties
                .OrderBy(p => PropertyGridDefaults.DisplayOf(p)?.Order ?? 0)
                .GroupBy(p => PropertyGridDefaults.GroupSegment(p, 0))
                .OrderBy(GroupOrderKey)
                .ToList();

        private static double GroupOrderKey(IGrouping<string, PropertyInfo> group) =>
            double.TryParse(
                PropertyGridDefaults.DisplayOf(group.First())?.GroupOrder,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0;

        protected PropertyGridBase()
        {
            // 把宿主自身注入嵌套生成器，使其能继承本网格的布局形态
            foreach (var gen in Generators.OfType<NestedPropertyGridGenerator>())
                gen.Owner = this;
        }

        #region 尺寸测量：无限高度防爆

        /// <summary>
        /// 属性网格常被放进"高度无限"的容器（StackPanel / 弹窗内容区），
        /// 不钳一下会把宿主窗口撑到几屏高。这里按可用窗口高度兜底。
        /// </summary>
        protected override Size MeasureOverride(Size constraint)
        {
            if (double.IsInfinity(constraint.Height) && MaxAutoHeight > 0)
            {
                // 高度无限时钳到 MaxAutoHeight，上限由宿主决定（默认 640），不再探测宿主窗口
                constraint = new Size(constraint.Width, Math.Max(200, MaxAutoHeight));
            }

            return base.MeasureOverride(constraint);
        }

        #endregion

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdatePropertyGrid();
        }

        /// <summary>重建整张表（外部也可调用：数据源结构变了但实例没换时）</summary>
        public void UpdatePropertyGrid()
        {
            if (BindingObject == null) return;
            if (GetTemplateChild("PART_TabControl") is not TabControl tabControl) return;

            // 每次重绘前先解绑上一轮挂的事件，防止内存泄漏
            _cleanupActions.Invoke();
            _cleanupActions = () => { };

            tabControl.Items.Clear();
            _builtTabs.Clear();
            _tabByKey = new Dictionary<string, TabItem>();

            var properties = PropertyGridDefaults.GetVisibleProperties(BindingObject);
            if (properties.Count == 0) return;

            // 只建 Tab 头，内容懒加载（选中才构建），避免一次性物化所有 Tab 的全部行
            foreach (var group in GroupProperties(properties))
            {
                var tabItem = new TabItem { Header = group.Key, Tag = group };
                tabControl.Items.Add(tabItem);
                _tabByKey[group.Key] = tabItem;
            }

            // 事件按"实例"挂：模板被换过时 TabControl 是新对象，必须重新挂（见 _hookedTabControl 的注释）
            if (!ReferenceEquals(tabControl, _hookedTabControl))
            {
                if (_hookedTabControl != null)
                    _hookedTabControl.SelectionChanged -= OnTabSelectionChanged;

                tabControl.SelectionChanged += OnTabSelectionChanged;
                _hookedTabControl = tabControl;
            }

            if (tabControl.Items.Count > 0 && tabControl.SelectedIndex == -1)
                tabControl.SelectedIndex = 0;

            // 兜底：事件若未触发也确保选中 Tab 有内容
            if (tabControl.SelectedItem is TabItem selected
                && selected.Tag is IGrouping<string, PropertyInfo> sg
                && !_builtTabs.Contains(selected))
            {
                BuildTabContent(selected, sg);
                _builtTabs.Add(selected);
            }
        }

        /// <summary>Tab 选中时按需构建内容（懒加载），已构建过的不再重复</summary>
        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not TabControl tabControl) return;
            if (tabControl.SelectedItem is not TabItem tabItem) return;
            if (tabItem.Tag is IGrouping<string, PropertyInfo> group && !_builtTabs.Contains(tabItem))
            {
                BuildTabContent(tabItem, group);
                _builtTabs.Add(tabItem);
            }
        }

        /// <summary>
        /// 精准刷新：只重建某个属性所属的 Tab，而不是整张表。
        /// 用于 <see cref="SuperDisplayAttribute.RequireRefresh"/> 触发的多态切换等场景。
        /// </summary>
        private void RebuildTabForProperty(string propertyName)
        {
            var prop = BindingObject?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return;

            var key = PropertyGridDefaults.GroupSegment(prop, 0);
            if (_tabByKey.TryGetValue(key, out var tabItem)
                && tabItem.Tag is IGrouping<string, PropertyInfo> group)
            {
                tabItem.Content = null;
                BuildTabContent(tabItem, group);
                _builtTabs.Add(tabItem);
            }
        }

        #region 数据源变更监听

        private static void OnBindingObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PropertyGridBase control) return;

            // 彻底断开旧对象的监听，严防内存泄漏
            if (control._currentNotifier != null)
                control._currentNotifier.PropertyChanged -= control.OnBindingObjectPropertyChanged;

            if (e.NewValue is INotifyPropertyChanged newNotifier)
            {
                newNotifier.PropertyChanged += control.OnBindingObjectPropertyChanged;
                control._currentNotifier = newNotifier;
            }
            else
            {
                control._currentNotifier = null;
            }

            control.UpdatePropertyGrid();
        }

        /// <summary>
        /// 属性变更 → 按需整表重绘。
        ///
        /// ★ 只有标记了 RequireRefresh = true 的属性才触发（多态切换那种"换了一个字段，
        ///   整张表都要重建"的场景）；普通属性走 WPF 绑定自己更新，不必重建 ——
        ///   否则用户每敲一个字就整表重绘一次，输入焦点会被抢走。
        ///
        /// ★ 线程兜底（**必须**）：这里是直接订阅 INotifyPropertyChanged，不是绑定引擎，
        ///   事件在**属性变更源线程**上执行（连接线程 / 轮询线程 / 流程引擎线程都可能）。
        ///   BindingObject 是 DependencyProperty，有线程亲和性，非 UI 线程读会直接抛
        ///   VerifyAccess 异常。统一先封送回 UI 线程再处理。
        /// </summary>
        private async void OnBindingObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => OnBindingObjectPropertyChanged(sender, e));
                return;
            }

            if (BindingObject == null || string.IsNullOrEmpty(e.PropertyName)) return;

            var propInfo = BindingObject.GetType().GetProperty(e.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            var displayAttr = propInfo?.GetCustomAttribute<SuperDisplayAttribute>();

            if (displayAttr == null || !displayAttr.RequireRefresh) return;

            if (_isRefreshPending) return;
            _isRefreshPending = true;

            // 等 50ms：让 ComboBox 下拉彻底关闭、WPF 的测量/排列/动画结束再重建
            await Task.Delay(50);

            // 异步投递而非同步 Invoke：关闭软件时 Dispatcher 会取消挂起操作，
            // 同步 Invoke 会把 TaskCanceledException 抛回属性变更源线程（插件/引擎层）
            VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
            {
                try
                {
                    // 只重建该属性所属的 Tab，而非整张表（其余 Tab 的选中态/滚动位置得以保留）
                    RebuildTabForProperty(e.PropertyName);
                }
                finally
                {
                    _isRefreshPending = false;
                }
            });
        }

        #endregion

        #region 生成与处理管线

        /// <summary>
        /// 按属性生成一个控件：命中顺序由 <see cref="IControlGenerator.Priority"/> 决定，
        /// **降序**（大者优先），Priority = 0 的 TypeGenerator 是兜底。
        /// </summary>
        public FrameworkElement CreateControl(PropertyInfo prop, object bindingSource, bool readOnly = false)
        {
            var generator = Generators
                .OrderByDescending(g => g.Priority)
                .FirstOrDefault(g => g.CanProcess(prop, prop.PropertyType, readOnly));

            if (generator != null)
                return generator.Create(prop, bindingSource, readOnly);

            // 兜底：类型没有对应生成器时给一个醒目但不刺眼的占位（走 Fluent 语义色令牌）
            var dangerBrush = Application.Current?.TryFindResource("FluentDangerBrush") as System.Windows.Media.Brush
                              ?? System.Windows.Media.Brushes.Red;
            return new TextBlock
            {
                Text = $"不支持的类型：{prop.PropertyType.Name}",
                Foreground = dangerBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 12,
            };
        }

        /// <summary>
        /// 走完整管线造出"一个属性单元格"：生成控件 → 布局 → 命令 → 校验 → 权限 → 外部处理器。
        /// 两种面板共用这一段，只有布局处理器按 <see cref="UseCardLayout"/> 分叉。
        /// </summary>
        protected UIElement BuildPropertyCell(PropertyInfo prop, bool isNested, SuperDisplayAttribute? display)
        {
            var control = isNested
                ? CreateControl(prop, prop.GetValue(BindingObject)!)
                : CreateControl(prop, BindingObject, display?.IsReadOnly ?? false);

            var context = new ControlContext
            {
                Property = prop,
                BindingSource = BindingObject,
                Control = control,
                WrapPanel = new StackPanel(),
                RootCellGrid = new Grid(),
                RegisterCleanup = action => _cleanupActions += action,
                SharedLabelGroup = SharedLabelGroup,
            };

            var pipeline = PropertyGridDefaults.CreateProcessors(display, UseCardLayout);
            pipeline.AddRange(Processors);

            foreach (var processor in pipeline)
                processor.Execute(context);

            return context.RootCellGrid;
        }

        #endregion
    }
}
