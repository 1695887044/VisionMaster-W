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
    /// 扁平式属性网格：一级分组（Tab）+ 表格化属性行（标签列带浅底，行间有分割线）。
    ///
    /// 全部公共逻辑（依赖属性、生成/处理管线、事件清理、重绘防抖、跨线程兜底）
    /// 都在 <see cref="PropertyGridBase"/>；这里只保留"内容怎么摆"。
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
            // 一级分组已由基类完成；这里只填充本 Tab 的内容（FlatPropertyGrid 的隐式样式已设 ItemContainerStyle）
            tabItem.Content = CreateFlatContent(group.ToList());
        }

        /// <summary>一个 Tab 内的全部属性行（外框 + 可滚动）</summary>
        private UIElement CreateFlatContent(List<PropertyInfo> properties)
        {
            var outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Margin = new Thickness(0, 10, 0, 10),
            };

            var mainStack = new StackPanel();
            Grid.SetIsSharedSizeScope(mainStack, true);

            foreach (var prop in properties)
            {
                var display = PropertyGridDefaults.DisplayOf(prop);
                var isNested = prop.GetCustomAttribute<PropertyItemAttribute>() != null;

                mainStack.Children.Add(BuildPropertyCell(prop, isNested, display));
            }

            outerBorder.Child = mainStack;

            return new ScrollViewer
            {
                Content = outerBorder,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12),
            };
        }
    }
}
