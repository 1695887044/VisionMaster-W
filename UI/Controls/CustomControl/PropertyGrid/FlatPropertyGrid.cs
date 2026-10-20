using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.Attributes;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    /// <summary>
    /// 扁平式属性网格：一级分组（Tab）+ 表格化属性行（标签列 + 值列，行间 1px 分隔线）。
    ///
    /// 全部公共逻辑（依赖属性、生成/处理管线、事件清理、重绘防抖、跨线程兜底）
    /// 都在 <see cref="PropertyGridBase"/>；这里只保留"内容怎么摆"。
    ///
    /// 【v1 设计基线】docs\UI设计基线\property-grid\design.html
    ///   · 外框圆角 12px、1px 描边（原来 4px 圆角 + 写死的 229,229,229）
    ///   · 内容区 14/18 内边距（原来 12）
    /// </summary>
    public class FlatPropertyGrid : PropertyGridBase
    {
        static FlatPropertyGrid()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FlatPropertyGrid), new FrameworkPropertyMetadata(typeof(FlatPropertyGrid)));
        }

        protected override bool UseCardLayout => false;

        protected override void BuildTabContent(TabItem tabItem, IGrouping<string, PropertyInfo> group)
        {
            tabItem.Content = CreateFlatContent(group.ToList());
        }

        /// <summary>一个 Tab 内的全部属性行（外框 + 可滚动）</summary>
        private UIElement CreateFlatContent(List<PropertyInfo> properties)
        {
            var mainStack = new StackPanel();
            Grid.SetIsSharedSizeScope(mainStack, true);

            for (var i = 0; i < properties.Count; i++)
            {
                var prop = properties[i];
                var display = PropertyGridDefaults.DisplayOf(prop);
                var isNested = prop.GetCustomAttribute<PropertyItemAttribute>() != null;

                mainStack.Children.Add(BuildPropertyCell(prop, isNested, display, isLastRow: i == properties.Count - 1));
            }

            // 嵌套子网格：不画外框（父级内联块已经有一层），也不加外边距 ——
            // 再画一层就是"卡片里套卡片"，层级反而看不清。
            if (IsNested)
            {
                return new ScrollViewer
                {
                    Content = mainStack,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(0),
                };
            }

            // 描边走令牌；取不到就回退中性灰（属性网格可能被没合并主题的宿主直接 new 出来）
            var borderBrush = (Application.Current?.TryFindResource("DialogBorderBrush") as Brush)
                              ?? new SolidColorBrush(Color.FromRgb(226, 232, 240));

            var outerBorder = new Border
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Background = (Application.Current?.TryFindResource("DialogSurfaceBrush") as Brush) ?? Brushes.White,
                Margin = new Thickness(0, 14, 0, 0),
                // 内容最后一行自己不带底边线，由外框收口；这里裁剪保证圆角不被行底色溢出
                ClipToBounds = true,
                Child = mainStack,
            };

            return new ScrollViewer
            {
                Content = outerBorder,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(18, 0, 18, 18),
            };
        }
    }
}
