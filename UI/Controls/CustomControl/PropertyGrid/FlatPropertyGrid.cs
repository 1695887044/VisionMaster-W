using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    public class FlatPropertyGrid : Control
    {
        const string DefaultName = "默认分组";

        public List<IControlGenerator> Generators { get; } = new List<IControlGenerator>();
        public List<IControlProcessor> Processors { get; } = new List<IControlProcessor>();

        public static readonly DependencyProperty BindingObjectProperty =
            DependencyProperty.Register(
                nameof(BindingObject),
                typeof(object),
                typeof(FlatPropertyGrid),
                new PropertyMetadata(null, OnBindingObjectChanged)
            );

        public object BindingObject
        {
            get => GetValue(BindingObjectProperty);
            set => SetValue(BindingObjectProperty, value);
        }

        static FlatPropertyGrid()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(FlatPropertyGrid),
                new FrameworkPropertyMetadata(typeof(FlatPropertyGrid))
            );
        }

        public FlatPropertyGrid()
        {
            Generators.Add(new EnumGenerator());
            Generators.Add(new StructValueGenerator());
            Generators.Add(new BoolStateGenerator());
            Generators.Add(new TypeGenerator());
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
                System.Diagnostics.Debug.WriteLine($"⚠️ [高度防爆] 捕获到无限高度，已动态修正为: {targetHeight}");
            }

            return base.MeasureOverride(constraint);
        }
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
            if (d is FlatPropertyGrid control)
                control.UpdatePropertyGrid();
        }

        private void UpdatePropertyGrid()
        {
            if (BindingObject == null)
                return;
            if (!(GetTemplateChild("PART_TabControl") is TabControl tabControl))
                return;

            tabControl.Items.Clear();

            var properties = BindingObject
                .GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => Attribute.IsDefined(p, typeof(SuperDisplayAttribute)))
                .ToList();

            if (properties.Count == 0)
                return;

            // 🚨 只有一层分组：直接按 GroupPath 的第一段分组，不管后面有没有斜杠
            var groupedProperties = properties
                .OrderBy(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Order ?? 0)
                .GroupBy(p =>
                {
                    var displayAttr = p.GetCustomAttribute<SuperDisplayAttribute>();
                    return displayAttr?.GroupPath?.Split('/').FirstOrDefault() ?? DefaultName;
                })
                .OrderBy(g =>
                    g.First().GetCustomAttribute<SuperDisplayAttribute>()?.GroupOrder ?? "0"
                );

            foreach (var group in groupedProperties)
            {
                var tabItem = new TabItem { Header = group.Key };
                // 直接将该组的属性生成一个扁平的网格列表，没有 Expander！
                tabItem.Content = CreateFlatGridContent(group.ToList());
                tabControl.Items.Add(tabItem);
            }

            if (tabControl.Items.Count > 0)
                tabControl.SelectedIndex = 0;
        }

        private UIElement CreateFlatGridContent(List<PropertyInfo> properties)
        {
            // 最外层的容器，提供外边框（左右上），底边框由内部每一行提供
            var outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)), // #E5E5E5
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(4, 4, 0, 0), // 顶部微圆角
                Margin = new Thickness(0, 10, 0, 10),
            };

            var mainStack = new StackPanel();
            Grid.SetIsSharedSizeScope(mainStack, true); // 保证所有行的标签对齐

            foreach (var prop in properties)
            {
                var display = prop.GetCustomAttribute<SuperDisplayAttribute>();

                var att = prop.GetCustomAttribute<PropertyItemAttribute>();
                var control =
                   att == null
                       ? CreateControl(prop, BindingObject, display.IsReadOnly)
                       : CreateControl(prop,  prop.GetValue(BindingObject));

                var context = new ControlContext
                {
                    Property = prop,
                    BindingSource = BindingObject,
                    Control = control,
                    WrapPanel = new StackPanel(),
                    RootCellGrid = new Grid(),
                };

                // 🚨 这里注入专为表格设计的 FlatLayoutProcessor
                var pipeline = new List<IControlProcessor>
                {
                    new FlatLayoutProcessor(display),
                    new CommandProcessor(),
                    new ValidationProcessor(),
                    new PermissionProcessor(),
                };
                pipeline.AddRange(Processors);

                foreach (var processor in pipeline)
                {
                    processor.Execute(context);
                }

                mainStack.Children.Add(context.RootCellGrid);
            }

            outerBorder.Child = mainStack;

            // 加上滚动条防止内容过多
            return new ScrollViewer
            {
                Content = outerBorder,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12),
            };
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

            return generator != null
                ? generator.Create(prop, bindingSource, readOnly)
                : new TextBlock { Text = $"Unsupported", Foreground = Brushes.Red };
        }
    }

    public class FlatLayoutProcessor : IControlProcessor
    {
        private readonly SuperDisplayAttribute _display;

        public FlatLayoutProcessor(SuperDisplayAttribute display)
        {
            _display = display;
        }

        public void Execute(ControlContext context)
        {
            var grid = context.RootCellGrid;
            var wrapper = context.WrapPanel;

            // 🚨 1. 在这里捕获属性上的 Icon 特性
            var iconAttr = context.Property.GetCustomAttribute<IconAttribute>();

            var rowBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "FlatLabelGroup" });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(16, 12, 32, 12),
                Background = new SolidColorBrush(Color.FromRgb(249, 249, 249))
            };

            // 🚨 2. 将 iconAttr 传给生成标签的方法
            labelBorder.Child = CreateLabel(context.Property, _display, iconAttr);
            Grid.SetColumn(labelBorder, 0);

            var controlBorder = new Border { Padding = new Thickness(16, 8, 16, 8) };
            context.Control.HorizontalAlignment = HorizontalAlignment.Stretch;
            context.Control.VerticalAlignment = VerticalAlignment.Center;

            if (!wrapper.Children.Contains(context.Control))
                wrapper.Children.Add(context.Control);

            controlBorder.Child = wrapper;
            Grid.SetColumn(controlBorder, 1);

            innerGrid.Children.Add(labelBorder);
            innerGrid.Children.Add(controlBorder);

            rowBorder.Child = innerGrid;
            grid.Children.Add(rowBorder);
        }

        private UIElement CreateLabel(PropertyInfo prop, SuperDisplayAttribute display, IconAttribute iconAttr)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // ==========================================
            // 🚨 核心逻辑：渲染 SVG Path 图标
            // ==========================================
            if (iconAttr != null && !string.IsNullOrEmpty(iconAttr.IconCode))
            {
                try
                {
                    var path = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(iconAttr.IconCode),
                        Fill = new SolidColorBrush(Color.FromRgb(0, 95, 184)), // Win11 经典蓝
                        Width = 14,
                        Height = 14,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 8, 0), // 右侧留白 8px
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    panel.Children.Add(path);
                }
                catch
                {
                    // 解析失败时给个红色感叹号防爆
                    panel.Children.Add(new TextBlock { Text = "⚠ ", Foreground = Brushes.Red, VerticalAlignment = VerticalAlignment.Center });
                }
            }
            else
            {
                // 没有图标时，塞入极其严谨的透明占位符（14宽 + 8间距 = 22像素）
                // 保证所有表格文本左侧绝对对齐！
                panel.Children.Add(new Border { Width = 22 });
            }

            // ==========================================
            // 文本部分
            // ==========================================
            var textBlock = new TextBlock
            {
                Text = display?.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51)), // #333333
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            panel.Children.Add(textBlock);

            // ==========================================
            // [Required] 必填项红星标记
            // ==========================================
            if (Attribute.IsDefined(prop, typeof(RequiredAttribute)))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = " *",
                    Foreground = new SolidColorBrush(Color.FromRgb(216, 59, 1)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            return panel;
        }
    }
}
