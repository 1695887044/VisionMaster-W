using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 递归嵌套生成器：当检测到属性是一个复杂的嵌套对象时，自动创建并嵌入一个子 PropertyGrid，
    /// 实现深度反射。
    ///
    /// 【v1 设计基线】docs\UI设计基线\property-grid\design.html
    ///
    /// 改造前：直接 `new CardPropertyGrid()` —— 子网格**自带 TabControl**，于是"链路参数"
    /// 这种嵌套块在卡片里又长出一条页签栏，层级关系反而读不出来（基线现状审查第 4 条）。
    ///
    /// 改造后：产出「左侧 3px 主色强调条 + 浅底 + 块头」的内联块，子网格仍在里面。
    /// 层级靠强调条与块头表达，不靠第二套页签。
    ///
    /// 【为什么还留着子网格】`GetVisibleProperties` / 生成器命中 / 处理器管线 / 分组排序
    /// 全在 `PropertyGridBase` 里，重写一份"扁平版递归"等于把整套管线复制一遍 ——
    /// 那是更贵的维护成本。这里只让子网格的**外观**服从父级形态。
    /// </summary>
    public class NestedPropertyGridGenerator : IControlGenerator
    {
        // 优先级设为 10，高于普通的 TypeGenerator，确保优先拦截
        public int Priority => 10;

        /// <summary>宿主网格，用于继承其布局形态（卡片/扁平）。由 PropertyGridBase 构造时注入。</summary>
        public PropertyGridBase? Owner { get; set; }

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly)
        {
            var att = prop.GetCustomAttribute<PropertyItemAttribute>();
            return att != null && (att.Type == typeof(Control) || att.Type == typeof(FrameworkElement));
        }

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            if (bindingSource == null)
            {
                return new TextBlock
                {
                    Text = "尚未初始化链路参数",
                    Foreground = PropertyGridMetrics.Brush("DialogWarningTextBrush", 180, 83, 9),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4),
                };
            }

            var isCard = Owner is { IsCardLayout: true };

            // 嵌套块里**一律用扁平式**，哪怕父级是卡片式。
            //
            // 【为什么】卡片式的左栏导航是"一级分组的入口"，父级已经有一条了；
            // 子网格再用卡片式就会在块里长出第二条导航栏（首轮出图实测：块内左侧
            // 又冒出一条 176px 的"链路参数 3"导航），层级反而更乱。
            // 嵌套块的层级已经由强调条 + 块头表达，内部只需一张干净的字段表。
            PropertyGridBase childGrid = new FlatPropertyGrid();
            childGrid.BindingObject = bindingSource;
            childGrid.Background = Brushes.Transparent;
            childGrid.BorderThickness = new Thickness(0);
            childGrid.Padding = new Thickness(0);
            // 告诉子网格"你在父级的块里"：它据此不画自己的外框（否则双层卡片）
            childGrid.IsNested = true;

            _ = isCard; // 形态只影响外层块的留白，见 BuildInlineBlock 的注释

            // 子网格的页签条不额外压制：只有一级分组时模板自己会收起（Items.Count == 1 的 DataTrigger），
            // 而"嵌套对象自己就有多个分组"的情况仍需保留切换能力。
            return BuildInlineBlock(prop, childGrid, bindingSource);
        }

        /// <summary>
        /// 内联块：左侧 3px 主色强调条 + 浅底 + 块头（↳ 图标 + 属性名 + 实际类型名）。
        /// 类型名只在它和属性名不同时显示（避免"链路参数 / 链路参数"这种重复）。
        /// </summary>
        private static FrameworkElement BuildInlineBlock(PropertyInfo prop, PropertyGridBase childGrid, object bindingSource)
        {
            var accent = PropertyGridMetrics.Brush("DialogAccentBrush", 14, 165, 233);
            var surface = PropertyGridMetrics.Brush("DialogRowAlternateBrush", 248, 250, 252);
            var border = PropertyGridMetrics.Brush("DialogBorderBrush", 226, 232, 240);
            var secondary = PropertyGridMetrics.Brush("DialogTextSecondaryBrush", 71, 85, 105);
            var muted = PropertyGridMetrics.Brush("DialogTextMutedBrush", 148, 163, 184);

            var display = prop.GetCustomAttribute<SuperDisplayAttribute>();
            var title = string.IsNullOrWhiteSpace(display?.Name) ? prop.Name : display!.Name;
            var typeName = bindingSource.GetType().Name;

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10),
            };
            header.Children.Add(new TextBlock
            {
                Text = "↳",
                FontSize = 12,
                Foreground = muted,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = secondary,
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (!string.Equals(typeName, title, StringComparison.Ordinal))
            {
                header.Children.Add(new TextBlock
                {
                    Text = typeName,
                    FontSize = 10,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
                    Foreground = muted,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(childGrid);

            var outer = new Border
            {
                Background = surface,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Child = content,
            };

            // 强调条贴左边（1px 描边内侧），用 3px 宽的 Border 而不是 BorderThickness 的左边 ——
            // BorderThickness 单边无法上色（BorderBrush 是整体一个），会连其它三边一起变蓝。
            var accentStrip = new Border
            {
                Width = 3,
                Background = accent,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(1, 1, 0, 1),
                IsHitTestVisible = false,
            };

            var host = new Grid();
            host.Children.Add(outer);
            host.Children.Add(accentStrip);
            return host;
        }
    }
}
