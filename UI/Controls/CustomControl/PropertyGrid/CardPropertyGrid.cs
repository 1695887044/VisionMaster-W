using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    /// <summary>
    /// 卡片式属性网格：**两级分组**（Tab → Expander）+ 12 栅格排版，
    /// 标签右对齐、无行线，观感更接近设置面板。
    ///
    /// 全部公共逻辑（依赖属性、生成/处理管线、事件清理、重绘防抖、跨线程兜底）
    /// 都在 <see cref="PropertyGridBase"/>；这里只保留"两级分组 + 栅格"这部分差异。
    /// </summary>
    public class CardPropertyGrid : PropertyGridBase
    {
        static CardPropertyGrid()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CardPropertyGrid), new FrameworkPropertyMetadata(typeof(CardPropertyGrid)));
        }

        protected override bool UseCardLayout => true;

        protected override void BuildTabContent(TabItem tabItem, IGrouping<string, PropertyInfo> group)
        {
            // 二级分组：GroupPath 第二段 → Expander 卡片。
            // 只有一级分组时（二级段全是默认组名）不套 Expander：
            // 否则每个 Tab 顶上都是一行无意义的"默认分组"折叠头。
            var subGroups = group.GroupBy(p => PropertyGridDefaults.GroupSegment(p, 1)).ToList();

            if (subGroups.Count == 1 && subGroups[0].Key == PropertyGridDefaults.DefaultGroupName)
            {
                tabItem.Content = CreateGridRows(subGroups[0].ToList());
                return;
            }

            tabItem.Content = CreateExpandersContent(subGroups);
        }

        private UIElement CreateExpandersContent(IEnumerable<IGrouping<string, PropertyInfo>> groups)
        {
            var mainPanel = new StackPanel();

            foreach (var group in groups)
            {
                var expander = new Expander
                {
                    IsExpanded = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    // Expander 默认模板的内容区不拉伸（按内容期望宽排），
                    // 值控件会被压成"贴着标签的一小条"——必须显式撑满
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 10),
                    Header = group.Key,
                    Content = CreateGridRows(group.ToList()),
                };

                mainPanel.Children.Add(expander);
            }

            // 滚动交给卡片模板的 ContentTemplate ScrollViewer（Horizontal 已 Disabled）。
            // 这里不能再包一层 ScrollViewer：内层横向默认 Auto 会以"无限宽"测量内容，
            // 12 栅格的 Star 列在无限宽下塌缩成内容宽，所有值控件被压成 30px 瘦条；
            // 外层(infinite height)里再套内层(infinite width)也是双重滚动的根子。
            return mainPanel;
        }

        /// <summary>12 栅格：按 ColSpan 占列，超过 12 就换行</summary>
        private UIElement CreateGridRows(List<PropertyInfo> properties)
        {
            // 不再加自身 Margin：主题隐式 Expander 样式的 ExpandSite 已带
            // Margin=4,4,0,0 + Padding=16,12,16,16，再叠 16 就是层层套皮，
            // 值列会被压到只剩几十像素（右半张卡片全在空转）
            var mainStack = new StackPanel();
            Grid.SetIsSharedSizeScope(mainStack, true);

            Grid? currentRow = null;
            var usedWeight = 0;

            foreach (var prop in properties)
            {
                var display = PropertyGridDefaults.DisplayOf(prop);

                var weight = display is { ColSpan: > 0 } ? display.ColSpan : PropertyGridDefaults.GridColumns;
                if (weight > PropertyGridDefaults.GridColumns) weight = PropertyGridDefaults.GridColumns;

                if (currentRow == null || usedWeight + weight > PropertyGridDefaults.GridColumns)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch };
                    for (var i = 0; i < PropertyGridDefaults.GridColumns; i++)
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    mainStack.Children.Add(currentRow);
                    usedWeight = 0;
                }

                var isNested = prop.GetCustomAttribute<PropertyItemAttribute>() != null;
                var cell = BuildPropertyCell(prop, isNested, display);

                Grid.SetColumn(cell, usedWeight);
                Grid.SetColumnSpan(cell, weight);
                currentRow.Children.Add(cell);
                usedWeight += weight;
            }

            return mainStack;
        }
    }
}
