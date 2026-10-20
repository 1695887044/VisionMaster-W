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

        /// <summary>
        /// 打开时想选中的页签（按 <see cref="SuperDisplayAttribute.Group"/> 第一段的标题匹配）。
        /// 空 / null = 落回第一页（原有行为）。
        ///
        /// 【为什么是依赖属性】合并式设置弹窗要让"不同菜单入口落到不同页签"
        /// （旧的「运行窗口设置」「系统参数设置」两个入口现在都指向同一个弹窗），
        /// 而页签是模板应用之后才建出来的 —— 视图侧没有别的时机能"选中某一页"。
        /// 页签是懒加载的，这里只改 SelectedItem，内容仍由 SelectionChanged 构建。
        /// </summary>
        public static readonly DependencyProperty SelectedGroupProperty =
            DependencyProperty.Register(
                nameof(SelectedGroup),
                typeof(string),
                typeof(PropertyGridBase),
                new PropertyMetadata(null, OnSelectedGroupChanged));

        public string? SelectedGroup
        {
            get => (string?)GetValue(SelectedGroupProperty);
            set => SetValue(SelectedGroupProperty, value);
        }

        private static void OnSelectedGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PropertyGridBase grid) grid.SelectGroup(e.NewValue as string);
        }

        /// <summary>
        /// 按页签标题选中一页。找不到（分组被改名 / 这一页整个消失）时**保持当前选中项不动**——
        /// 落到第一页比"选中一个不存在的页"好，也比抛异常好（分组名是界面知识，改它不该炸）。
        /// </summary>
        public bool SelectGroup(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return false;
            if (GetTemplateChild("PART_TabControl") is not TabControl tabControl) return false;

            foreach (var item in tabControl.Items)
            {
                if (item is not TabItem tab) continue;
                if (!string.Equals(tab.Header as string, header, StringComparison.Ordinal)) continue;

                tabControl.SelectedItem = tab;

                // 已经是选中项时 SelectionChanged 不会触发，懒构建要在这里补一次
                if (tab.Tag is IGrouping<string, PropertyInfo> group && !_builtTabs.Contains(tab))
                {
                    BuildTabContent(tab, group);
                    _builtTabs.Add(tab);
                }

                return true;
            }

            return false;
        }

        /// <summary>true = 卡片式布局（标签右对齐 + 12 栅格）；false = 扁平表格式布局</summary>
        protected abstract bool UseCardLayout { get; }

        /// <summary>程序集内可见的布局形态判断，供嵌套生成器等同程序集组件读取</summary>
        internal bool IsCardLayout => UseCardLayout;

        /// <summary>
        /// 是否是"嵌在另一个属性网格里"的子网格。
        ///
        /// 由 <see cref="NestedPropertyGridGenerator"/> 置位。子网格**不再画自己的外框** ——
        /// 它已经被父级的内联块（强调条 + 浅底 + 圆角描边）包住了，再画一层就是双层卡片，
        /// 屏幕上看起来像"卡片里套了个卡片"，层级反而更乱。
        /// </summary>
        internal bool IsNested { get; set; }

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
        ///
        /// 【空态例外】空态只有一条说明，**不该撑满**容器（设计基线要点 13：
        /// "空态窗口按内容收高"）。所以空态时把高度约束交给内容自身，
        /// 并返回内容的 DesiredSize —— 宿主若用 SizeToContent 就会自动收高。
        /// </summary>
        protected override Size MeasureOverride(Size constraint)
        {
            if (IsEmptyState)
            {
                // 宽度仍按可用宽（说明文字要能换行），高度放开给内容。
                // 用 base 的**返回值**而不是 DesiredSize：DesiredSize 是本方法返回之后才由
                // 框架写回的，在这里读它读到的是上一轮的值（首轮就是 0）。
                var contentConstraint = new Size(constraint.Width, double.PositiveInfinity);
                var contentSize = base.MeasureOverride(contentConstraint);
                return new Size(
                    double.IsInfinity(constraint.Width) ? contentSize.Width : constraint.Width,
                    contentSize.Height);
            }

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

        /// <summary>
        /// 重建整张表（外部也可调用：数据源结构变了但实例没换时）。
        /// <paramref name="preserveSelectedTab"/> = 按页签标题保住当前选中项：
        /// 整表重建会换掉全部 TabItem 实例，不保就会跳回第一页。
        /// </summary>
        public void UpdatePropertyGrid(bool preserveSelectedTab = false)
        {
            if (BindingObject == null) return;
            if (GetTemplateChild("PART_TabControl") is not TabControl tabControl) return;

            // 记下当前选中的页签标题：页签是懒加载的，跳走再跳回会让用户以为"改完就跳页了"
            var previousHeader = preserveSelectedTab
                ? (tabControl.SelectedItem as TabItem)?.Header as string
                : null;

            // 每次重绘前先解绑上一轮挂的事件，防止内存泄漏
            _cleanupActions.Invoke();
            _cleanupActions = () => { };

            tabControl.Items.Clear();
            _builtTabs.Clear();

            var properties = PropertyGridDefaults.GetVisibleProperties(BindingObject);

            // 空态：没有可见属性时给出明确说明，而不是留一片空白。
            // 契约不变（GetVisibleProperties 仍是唯一判据），只是把"零结果"画出来。
            //
            // 实现取巧但无副作用：塞一个 Header 为空的 TabItem 并选中它。
            // 两个模板都有「Items.Count == 1 → 折叠页签条」的触发器，所以那条空页签头不会露出来；
            // 内容区照常由 ContentPresenter 渲染（ContentSource=SelectedContent）。
            if (properties.Count == 0)
            {
                var emptyTab = new TabItem { Header = string.Empty, Content = BuildEmptyState() };
                tabControl.Items.Add(emptyTab);
                tabControl.SelectedItem = emptyTab;
                IsEmptyState = true;
                return;
            }

            IsEmptyState = false;

            // 只建 Tab 头，内容懒加载（选中才构建），避免一次性物化所有 Tab 的全部行
            TabItem? restoredTab = null;
            foreach (var group in GroupProperties(properties))
            {
                var groupList = group.ToList();
                var tabItem = new TabItem { Header = group.Key, Tag = group };

                // 卡片式左栏显示每组的属性条数（模板绑这个附加属性，0 = 不显示）
                PropertyGridAttach.SetGroupCount(tabItem, groupList.Count);

                tabControl.Items.Add(tabItem);
                if (previousHeader != null && string.Equals(group.Key, previousHeader, StringComparison.Ordinal))
                    restoredTab = tabItem;
            }

            // 事件按"实例"挂：模板被换过时 TabControl 是新对象，必须重新挂（见 _hookedTabControl 的注释）
            if (!ReferenceEquals(tabControl, _hookedTabControl))
            {
                if (_hookedTabControl != null)
                    _hookedTabControl.SelectionChanged -= OnTabSelectionChanged;

                tabControl.SelectionChanged += OnTabSelectionChanged;
                _hookedTabControl = tabControl;
            }

            // 恢复原选中页签；找不到（该分组整体消失）才落回第一页
            if (restoredTab != null)
                tabControl.SelectedItem = restoredTab;
            else if (tabControl.Items.Count > 0 && tabControl.SelectedIndex == -1)
                tabControl.SelectedIndex = 0;

            // 宿主指定的页签优先于"落回第一页"：入口分页（见 SelectedGroup 的注释）。
            // 放在这里而不是更前面，是因为它必须盖住上面那两条兜底。
            if (!string.IsNullOrWhiteSpace(SelectedGroup))
                SelectGroup(SelectedGroup);

            // 兜底：事件若未触发也确保选中 Tab 有内容
            if (tabControl.SelectedItem is TabItem selected
                && selected.Tag is IGrouping<string, PropertyInfo> sg
                && !_builtTabs.Contains(selected))
            {
                BuildTabContent(selected, sg);
                _builtTabs.Add(selected);
            }
        }

        /// <summary>
        /// 当前是否处于"没有可显示属性"的空态（<see cref="BindingObject"/> 为 null 或
        /// 没有任何属性满足 <c>[SuperDisplay(Visible = true)]</c>）。
        ///
        /// 【为什么是依赖属性】两个控件模板要用它做触发器（卡片式在空态下必须收起左栏导航，
        /// 否则 176px 的空导航条和一条空态说明并排出现）。普通 CLR 属性无法被模板绑定。
        ///
        /// 【为什么对外公开】宿主可能想据此收高窗口（`EasyDialog` 的 SizeToContent）。
        /// WPF 的 <see cref="MeasureOverride"/> 只在高度无限时往下钳，**不会**因为属性为 0 而收缩 ——
        /// 设计基线要求的"空态不留空白"需要宿主或调用方配合，这个属性就是那个判据。
        /// </summary>
        public static readonly DependencyPropertyKey IsEmptyStatePropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsEmptyState),
                typeof(bool),
                typeof(PropertyGridBase),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsEmptyStateProperty = IsEmptyStatePropertyKey.DependencyProperty;

        public bool IsEmptyState
        {
            get => (bool)GetValue(IsEmptyStateProperty);
            private set => SetValue(IsEmptyStatePropertyKey, value);
        }

        /// <summary>
        /// 空态视图：图标 + 标题 + 说明。只描述契约，不臆造"为什么没有属性"。
        /// 颜色一律 TryFindResource（宿主可能没合并本库主题，抛异常等于控件不可用）。
        /// </summary>
        private static UIElement BuildEmptyState()
        {
            var muted = (Application.Current?.TryFindResource("DialogTextMutedBrush") as System.Windows.Media.Brush)
                        ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            var secondary = (Application.Current?.TryFindResource("DialogTextSecondaryBrush") as System.Windows.Media.Brush)
                            ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
            var primary = (Application.Current?.TryFindResource("DialogTextPrimaryBrush") as System.Windows.Media.Brush)
                          ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
            var surface = (Application.Current?.TryFindResource("DialogRowHoverBrush") as System.Windows.Media.Brush)
                          ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));

            var iconHost = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(12),
                Background = surface,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // 图标：一个"空表格"轮廓（三横一竖），与"属性面板里没有字段"语义对应。
            // 用 Path 而不是字体图标：属性网格可能被没合并主题的宿主 new 出来，
            // 字体家族取不到就是豆腐块，几何路径在任何环境都画得出来。
            var icon = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M3,4 H17 M3,9 H17 M3,14 H11 M6,4 V14"),
                Stroke = muted,
                StrokeThickness = 1.4,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                Width = 18,
                Height = 18,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            iconHost.Child = icon;

            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 48, 24, 48),
                MaxWidth = 380,
            };

            panel.Children.Add(iconHost);
            panel.Children.Add(new TextBlock
            {
                Text = "该对象没有可显示的属性",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = primary,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "属性面板只渲染标了 [SuperDisplay] 且 Visible = true 的公开实例属性。当前绑定对象的字段都不满足条件。",
                FontSize = 11.5,
                Foreground = secondary,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });

            // 说明文字的第二行用更弱的色（设计稿里两行同色，但这里保留层级：标题 > 说明）
            ((TextBlock)panel.Children[2]).Foreground = muted;

            return panel;
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
        /// 【口径已定】<see cref="SuperDisplayAttribute.RequireRefresh"/> = **整表重建**，
        /// 不是"只重建该属性所在的页签"。曾经为省一次物化改成按属性分组精准重建，
        /// 结果多态属性与它驱动的那个子对象不在同一个页签时（典型：
        /// <c>CommunicationConfig.Protocol</c> 在「基本设置」、它换掉的 <c>Config</c> 在「链路配置」）
        /// 被驱动的那个页签**永远不会重建** → 用户把 ModbusRtu 选上了，界面上仍是 IP/端口。
        /// 更隐蔽的是它不会自愈：旧页签仍躺在 _builtTabs 里，来回切页签也不会重走懒构建。
        /// 语义上 RequireRefresh 本来就是"整张表可能换形"，只有整表重建盖得住它。
        ///
        /// ★ 普通属性不触发（走 WPF 绑定自己更新）：否则每敲一个字就整表重绘，输入焦点会被抢走。
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
                    // 整表重建：多态切换可能换掉任意页签里的嵌套对象，只重建触发者那一页会漏
                    UpdatePropertyGrid(preserveSelectedTab: true);
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
        protected UIElement BuildPropertyCell(PropertyInfo prop, bool isNested, SuperDisplayAttribute? display, bool isLastRow = false)
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
                IsLastRow = isLastRow,
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
