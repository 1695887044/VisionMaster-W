using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.Attributes; // 确保引用了最新的特性类
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    public class CardPropertyGrid : Control
    {
        private const string DefaultName = "默认分组";

        public List<IControlGenerator> Generators { get; } = new();
        public List<IControlProcessor> Processors { get; } = new();

        #region 🌟 核心优化字段 (防爆、防泄漏、防卡顿)
        // 用于存储卸载事件的委托，每次重绘前清理僵尸事件
        private Action _cleanupActions = () => { };
        // 数据源属性监听器缓存
        private INotifyPropertyChanged? _currentNotifier;
        // 重绘防抖节流标志
        private bool _isRefreshPending = false;
        #endregion

        public static readonly DependencyProperty BindingObjectProperty =
            DependencyProperty.Register(
                nameof(BindingObject),
                typeof(object),
                typeof(CardPropertyGrid),
                new PropertyMetadata(null, OnBindingObjectChanged)
            );

        public object BindingObject
        {
            get => GetValue(BindingObjectProperty);
            set => SetValue(BindingObjectProperty, value);
        }

        static CardPropertyGrid()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CardPropertyGrid), new FrameworkPropertyMetadata(typeof(CardPropertyGrid)));
        }

        public CardPropertyGrid()
        {
            // 注册默认生成器
            Generators.Add(new NestedPropertyGridGenerator());
            Generators.Add(new EnumGenerator());
            Generators.Add(new StructValueGenerator());
            Generators.Add(new BoolStateGenerator());
            Generators.Add(new TypeGenerator());
        }

        #region 尺寸测量与安全拦截
        protected override Size MeasureOverride(Size constraint)
        {
            if (double.IsInfinity(constraint.Height))
            {
                double targetHeight = SystemParameters.WorkArea.Height - 300; // 兜底高度
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null && mainWindow.ActualHeight > 100)
                {
                    targetHeight = mainWindow.ActualHeight - 250;
                }
                else
                {
                    var window = Window.GetWindow(this);
                    if (window != null && window.ActualHeight > 100) targetHeight = window.ActualHeight - 250;
                }

                constraint = new Size(constraint.Width, Math.Max(200, targetHeight));
                System.Diagnostics.Debug.WriteLine($"⚠️ [CardPropertyGrid 高度防爆] 捕获到无限高度，已动态修正为: {targetHeight}");
            }

            return base.MeasureOverride(constraint);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdatePropertyGrid();
        }
        #endregion

        #region 🌟 动态刷新架构核心逻辑 (INotifyPropertyChanged 拦截)
        private static void OnBindingObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CardPropertyGrid control)
            {
                // 彻底断开旧对象的监听，严防内存泄漏
                if (control._currentNotifier != null)
                {
                    control._currentNotifier.PropertyChanged -= control.OnBindingObjectPropertyChanged;
                }

                // 挂载新对象的监听
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
        }

        // 🌟 注意：方法签名加上了 async 关键字！
        private async void OnBindingObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (BindingObject == null || string.IsNullOrEmpty(e.PropertyName)) return;

            var propInfo = BindingObject.GetType().GetProperty(e.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            var displayAttr = propInfo?.GetCustomAttribute<SuperDisplayAttribute>();

            // 拦截：只有明确标记 RequireRefresh = true 的属性，才触发全体重绘
            if (displayAttr != null && displayAttr.RequireRefresh)
            {
                if (_isRefreshPending) return;
                _isRefreshPending = true;

                // =================================================================
                // 🛡️ 终极防爆机制：异步逃逸
                // 让当前方法立刻返回，彻底让出 UI 线程。
                // 强行等待 50 毫秒，让 ComboBox 的下拉弹窗完全关闭，让 WPF 内部所有的
                // 测量(Measure)、排列(Arrange) 和动画彻底死透！
                // =================================================================
                await Task.Delay(50);

                // 50毫秒后，天下太平，我们再安全地切回 UI 线程进行毁灭性重绘
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        UpdatePropertyGrid();
                    }
                    finally
                    {
                        _isRefreshPending = false;
                    }
                });
            }
        }
        #endregion

        #region UI 层级构建逻辑 (Tab -> Expander -> 12栅格)
        private void UpdatePropertyGrid()
        {
            if (BindingObject == null || !(GetTemplateChild("PART_TabControl") is TabControl tabControl))
                return;

            // 🌟 每次重绘前，执行清理操作（解绑丢失焦点的验证等事件），严防内存泄漏
            _cleanupActions.Invoke();
            _cleanupActions = () => { };

            tabControl.Items.Clear();

            var properties = BindingObject.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => Attribute.IsDefined(p, typeof(SuperDisplayAttribute)))
                .ToList();

            if (!properties.Any()) return;

            // 一级分组 (解析 GroupPath 的第一段)
            var groupedProperties = properties
                .OrderBy(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Order ?? 0)
                .GroupBy(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.GroupPath?.Split('/').ElementAtOrDefault(0) ?? DefaultName)
                .OrderBy(g => g.First().GetCustomAttribute<SuperDisplayAttribute>()?.GroupOrder ?? "0");

            foreach (var group in groupedProperties)
            {
                var tabItem = new TabItem { Header = group.Key };

                // 二级分组 (解析 GroupPath 的第二段)
                var secondLevelGroups = group.GroupBy(p =>
                    p.GetCustomAttribute<SuperDisplayAttribute>()?.GroupPath?.Split('/').ElementAtOrDefault(1) ?? DefaultName);

                tabItem.Content = CreateGroupContent(secondLevelGroups);
                tabControl.Items.Add(tabItem);
            }

            if (tabControl.Items.Count > 0 && tabControl.SelectedIndex == -1)
                tabControl.SelectedIndex = 0;
        }

        // 组装二级分组 (生成 Expander 卡片)
        private UIElement CreateGroupContent(IEnumerable<IGrouping<string, PropertyInfo>> groups)
        {
            var mainPanel = new StackPanel();
            foreach (var group in groups)
            {
                var groupContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

                var expander = new Expander
                {
                    IsExpanded = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 10),
                    Header = group.Key,
                };

                var rowsContent = CreateGroupContent(group.ToList());

                groupContainer.Children.Add(rowsContent);
                expander.Content = groupContainer;
                mainPanel.Children.Add(expander);
            }

            // 加入滚动条，适配多 Expander 情况
            return new ScrollViewer
            {
                Content = mainPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12)
            };
        }

        // 核心：动态 12 栅格排版
        private UIElement CreateGroupContent(List<PropertyInfo> properties)
        {
            var mainStack = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            Grid.SetIsSharedSizeScope(mainStack, true);

            Grid? currentActiveGrid = null;
            int currentUsedWeight = 0;

            foreach (var prop in properties)
            {
                var display = prop.GetCustomAttribute<SuperDisplayAttribute>();
                int weight = (display != null && display.ColSpan > 0) ? display.ColSpan : 12;
                if (weight > 12) weight = 12;

                // 换行逻辑：当前行满载或尚未初始化
                if (currentActiveGrid == null || currentUsedWeight + weight > 12)
                {
                    currentActiveGrid = new Grid
                    {
                        Margin = new Thickness(0, 0, 0, 12),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };

                    for (int i = 0; i < 12; i++)
                    {
                        currentActiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }

                    mainStack.Children.Add(currentActiveGrid);
                    currentUsedWeight = 0;
                }

                // 🌟 将生成单个控件的逻辑抛给流水线
                var cell = CreateSinglePropertyCell(prop, display!);

                Grid.SetColumn(cell, currentUsedWeight);
                Grid.SetColumnSpan(cell, weight);

                currentActiveGrid.Children.Add(cell);
                currentUsedWeight += weight;
            }
            return mainStack;
        }

        // 单一属性构造流
        private UIElement CreateSinglePropertyCell(PropertyInfo prop, SuperDisplayAttribute display)
        {
            var att = prop.GetCustomAttribute<PropertyItemAttribute>();
            var control = att == null
                    ? CreateControl(prop, BindingObject, display.IsReadOnly)
                    : CreateControl(prop, prop.GetValue(BindingObject)!);

            var context = new ControlContext
            {
                Property = prop,
                BindingSource = BindingObject,
                Control = control,
                WrapPanel = new StackPanel(),
                RootCellGrid = new Grid(),
                RegisterCleanup = action => _cleanupActions += action // 🌟 暴露给 Processor 的清理注册入口
            };

            var pipeline = new List<IControlProcessor>
            {
                new LayoutProcessor(display), // 这里调用了你原本写的卡片风格 LayoutProcessor
                new CommandProcessor(),
                new ValidationProcessor(),
                new PermissionProcessor(),
            };
            pipeline.AddRange(Processors);

            foreach (var processor in pipeline)
            {
                processor.Execute(context);
            }

            return context.RootCellGrid;
        }

        public FrameworkElement CreateControl(PropertyInfo prop, object bindingSource, bool readOnly = false)
        {
            var generator = Generators
                .OrderBy(g => g.Priority)
                .FirstOrDefault(g => g.CanProcess(prop, prop.PropertyType, readOnly));

            if (generator != null)
                return generator.Create(prop, bindingSource, readOnly);

            return new TextBlock
            {
                Text = $"Unsupported: {prop.PropertyType.Name}",
                Foreground = Brushes.Red,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
        }
        #endregion
    }
}