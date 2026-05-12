using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    public class CardPropertyGrid : Control
    {
        const string DefaultName = "默认分组";

        public List<IControlGenerator> Generators { get; } = new List<IControlGenerator>();
        public List<IControlProcessor> Processors { get; } = new List<IControlProcessor>();

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
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(CardPropertyGrid),
                new FrameworkPropertyMetadata(typeof(CardPropertyGrid))
            );
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (double.IsInfinity(constraint.Height))
            {
                double targetHeight = 600; // 默认硬底线
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null && mainWindow.ActualHeight > 100)
                {
                    targetHeight = mainWindow.ActualHeight - 250;
                }
                else
                {
                    // 尝试 2：获取当前所在窗体的高度
                    var window = Window.GetWindow(this);
                    if (window != null && window.ActualHeight > 100)
                    {
                        targetHeight = window.ActualHeight - 250;
                    }
                    else
                    {
                        // 尝试 3：如果窗体还没开始渲染，直接去取系统屏幕的工作区高度！
                        // 减去大概 300 像素留给任务栏和程序的头部
                        targetHeight = SystemParameters.WorkArea.Height - 300;
                    }
                }

                // 强制修正为计算出的动态高度
                constraint = new Size(constraint.Width, targetHeight);
                System.Diagnostics.Debug.WriteLine(
                    $"⚠️ [高度防爆] 捕获到无限高度，已动态修正为: {targetHeight}"
                );
            }

            return base.MeasureOverride(constraint);
        }

        public CardPropertyGrid()
        {
            // 注册默认生成器
            Generators.Add(new EnumGenerator());
            Generators.Add(new StructValueGenerator());
            Generators.Add(new BoolStateGenerator());
            Generators.Add(new TypeGenerator());
        }

        // ==========================================
        // 生命周期：模板应用完成后必须重绘
        // ==========================================
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdatePropertyGrid();
        }

        private static void OnBindingObjectChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (d is CardPropertyGrid control)
                control.UpdatePropertyGrid();
        }

        private void UpdatePropertyGrid()
        {
            if (BindingObject == null)
                return;

            if (!(GetTemplateChild("PART_TabControl") is TabControl tabControl))
                return;

            tabControl.Items.Clear();

            // 1. 获取带有 SuperDisplayAttribute 的所有公共属性
            var properties = BindingObject
                .GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => Attribute.IsDefined(p, typeof(SuperDisplayAttribute)))
                .ToList();

            if (properties.Count == 0)
                return;

            // 2. 一级分组 + 排序
            var groupedProperties = properties
                .OrderBy(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Order ?? 0)
                .GroupBy(p =>
                {
                    var displayAttr = p.GetCustomAttribute<SuperDisplayAttribute>();
                    return displayAttr?.GroupPath?.Split('/').ElementAtOrDefault(0) ?? DefaultName;
                })
                .OrderBy(g =>
                    g.First().GetCustomAttribute<SuperDisplayAttribute>()?.GroupOrder ?? "0"
                );

            // 3. 创建 TabItem 和二级内容
            foreach (var group in groupedProperties)
            {
                var tabItem = new TabItem { Header = group.Key };

                var secondLevelGroups = group.GroupBy(p =>
                {
                    var attr = p.GetCustomAttribute<SuperDisplayAttribute>();
                    return attr?.GroupPath?.Split('/').ElementAtOrDefault(1) ?? DefaultName;
                });

                tabItem.Content = CreateGroupContent(secondLevelGroups);
                tabControl.Items.Add(tabItem);
            }

            if (tabControl.Items.Count > 0)
                tabControl.SelectedIndex = 0;
        }

        // 组装二级分组 (Expander)
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

                var propertiesList = group.ToList();
                var rowsContent = CreateGroupContent(propertiesList);

                groupContainer.Children.Add(rowsContent);
                expander.Content = groupContainer;
                mainPanel.Children.Add(expander);
            }
            return mainPanel;
        }

        // ==========================================
        // 核心：动态栅格排版 (响应 ColSpan & 对齐)
        // ==========================================
        private UIElement CreateGroupContent(List<PropertyInfo> properties)
        {
            var mainStack = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            // 开启标签共享尺寸，保证左侧标签完美垂直对齐
            Grid.SetIsSharedSizeScope(mainStack, true);

            Grid currentActiveGrid = null;
            int currentUsedWeight = 0;

            foreach (var prop in properties)
            {
                var display = prop.GetCustomAttribute<SuperDisplayAttribute>();
                int weight = (display != null && display.ColSpan > 0) ? display.ColSpan : 12;
                if (weight > 12)
                    weight = 12;

                // 换行逻辑
                if (currentActiveGrid == null || currentUsedWeight + weight > 12)
                {
                    currentActiveGrid = new Grid
                    {
                        Margin = new Thickness(0, 0, 0, 12), // 行下边距
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };

                    // 初始化 12 列网格基座
                    for (int i = 0; i < 12; i++)
                    {
                        currentActiveGrid.ColumnDefinitions.Add(
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                        );
                    }

                    mainStack.Children.Add(currentActiveGrid);
                    currentUsedWeight = 0;
                }

                // 生成单独的控件单元格
                var cell = CreateSinglePropertyCell(prop, display);

                Grid.SetColumn(cell, currentUsedWeight);
                Grid.SetColumnSpan(cell, weight);

                currentActiveGrid.Children.Add(cell);
                currentUsedWeight += weight;
            }
            return mainStack;
        }

        // 生成单个属性节点 (经由流水线)
        private UIElement CreateSinglePropertyCell(PropertyInfo prop, SuperDisplayAttribute display)
        {
            var att = prop.GetCustomAttribute<PropertyItemAttribute>();
            var control =
                att == null
                    ? CreateControl(prop, BindingObject, display.IsReadOnly)
                    : CreateControl(prop, prop.GetValue(BindingObject));

            var context = new ControlContext
            {
                Property = prop,
                BindingSource = BindingObject,
                Control = control,
                WrapPanel = new StackPanel(),
                RootCellGrid = new Grid(),
            };

            // 流水线处理
            var pipeline = new List<IControlProcessor>
            {
                new LayoutProcessor(display),
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

        public FrameworkElement CreateControl(
            PropertyInfo prop,
            object bindingSource,
            bool readOnly = false
        )
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
    }
}
