using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 属性行的布局处理器（合并了原先的 LayoutProcessor 与 FlatLayoutProcessor）。
    ///
    /// 两者做的是同一件事：把"标签 + 控件"摆成一行的两列，差别只在**皮肤**
    /// （扁平：左侧带底色与竖分割线的表格行；卡片：右对齐标签 + 无框的栅格单元）。
    /// 分成两个类之后，"补一个必填红星""换个主题色"都要改两处。
    /// 这里用 <see cref="_cardStyle"/> 参数化皮肤，逻辑只有一份。
    /// </summary>
    public class PropertyGridLayoutProcessor : IControlProcessor
    {
        private readonly SuperDisplayAttribute? _display;
        private readonly bool _cardStyle;

        /// <param name="display">该属性的展示特性（名字 / 图标 / 描述）</param>
        /// <param name="cardStyle">true = 卡片式皮肤，false = 扁平表格皮肤</param>
        public PropertyGridLayoutProcessor(SuperDisplayAttribute? display, bool cardStyle)
        {
            _display = display;
            _cardStyle = cardStyle;
        }

        public void Execute(ControlContext context)
        {
            var grid = context.RootCellGrid;
            var wrapper = context.WrapPanel;

            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            var label = CreateLabel(context.Property);
            // 布尔行（开关）：整行可点 + 贴标签排布，见下面各处注释
            var toggle = context.Control as System.Windows.Controls.Primitives.ToggleButton;

            // 对齐：开关这类固定尺寸控件**贴着标签排**，不再居中在整行空白里 ——
            // 居中的后果是"开关离标签很远，点之前还得先找"。可拉伸的编辑器（文本框/下拉）
            // 保持 Stretch 占满；两种对齐各自在全表所有行保持一致。
            context.Control.HorizontalAlignment = toggle != null
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Stretch;
            context.Control.VerticalAlignment = VerticalAlignment.Center;

            // 无障碍：给控件补名字与说明（屏幕阅读器读这颗开关时不至于只说"切换"）。
            // ToggleButton 自带 Toggle 模式（等价 role="switch"，IsChecked 即 aria-checked），
            // Space 由它自己处理；名字/说明补齐后语义就完整了。
            System.Windows.Automation.AutomationProperties.SetName(context.Control, _display?.Name ?? context.Property.Name);
            if (!string.IsNullOrWhiteSpace(_display?.Description))
                System.Windows.Automation.AutomationProperties.SetHelpText(context.Control, _display!.Description);

            if (!wrapper.Children.Contains(context.Control))
                wrapper.Children.Add(context.Control);

            if (_cardStyle)
            {
                grid.Margin = new Thickness(0, 0, 16, 0);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = context.SharedLabelGroup });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                label.HorizontalAlignment = HorizontalAlignment.Right;
                label.Margin = new Thickness(0, 0, 16, 0);

                Grid.SetColumn(label, 0);
                Grid.SetColumn(wrapper, 1);
                grid.Children.Add(label);
                grid.Children.Add(wrapper);
                MakeRowClickable(label, toggle);
                return;
            }

            // 扁平：整行一条底部分隔线，标签列与值列**同底色**，层级只靠 1px 分隔线 ——
            // 原来左灰右白两块底色硬切，看着割裂；统一底色后"这是两组格"的信息交给分隔线。
            // 分割线色一律走 Fluent 令牌（取不到才回退），换肤/深色模式会跟着变。
            var borderColor = (Application.Current?.TryFindResource("FluentBorderBrush") as Brush)
                              ?? new SolidColorBrush(Color.FromRgb(229, 229, 229));

            var rowBorder = new Border
            {
                BorderBrush = borderColor,
                BorderThickness = new Thickness(0, 0, 0, 1),
                // 刻意给透明底：WPF 里 Background=null 的元素**不参与命中测试**，
                // 不给透明底的话"点行内空白处切换开关"就是一句空话
                Background = Brushes.Transparent,
            };
            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = context.SharedLabelGroup });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBorder = new Border
            {
                BorderBrush = borderColor,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(16, 12, 32, 12),
                Child = label,
                // 刻意不上背景色（原来是 SidebarBg 浅灰）：见上，同底色 + 分隔线
            };
            Grid.SetColumn(labelBorder, 0);

            var controlBorder = new Border { Padding = new Thickness(16, 8, 16, 8), Child = wrapper };
            Grid.SetColumn(controlBorder, 1);

            innerGrid.Children.Add(labelBorder);
            innerGrid.Children.Add(controlBorder);
            rowBorder.Child = innerGrid;
            grid.Children.Add(rowBorder);

            // 整行可点：点行内任意位置（含「启用」文字）都能切换，热区不再只有那一小颗
            MakeRowClickable(rowBorder, toggle);
        }

        /// <summary>
        /// 让布尔行整行可点：点容器任意空白处 = 切换开关。
        ///
        /// 为什么不会"点一下切两次"：ToggleButton 自己会把 MouseLeftButtonUp 标成 Handled，
        /// 这里挂的是**不接 Handled** 的普通监听 —— 点在开关上时根本走不到这里，
        /// 只有点在标签/空白处才触发，两条路径天然互斥。
        /// 非布尔行直接返回：整行可点只对"开关"这种二元控件有意义。
        /// </summary>
        private static void MakeRowClickable(FrameworkElement container, System.Windows.Controls.Primitives.ToggleButton? toggle)
        {
            if (toggle == null) return;

            container.Cursor = System.Windows.Input.Cursors.Hand;
            container.MouseLeftButtonUp += (_, e) =>
            {
                toggle.IsChecked = !(toggle.IsChecked ?? false);
                e.Handled = true;
            };
        }

        /// <summary>
        /// 标签行：图标 + 名称 + 必填红星。
        /// 主题色一律 TryFindResource（取不到就回退），绝不用 FindResource ——
        /// 属性网格可能被没有合并本库主题的宿主直接 new 出来，抛异常等于控件不可用。
        /// </summary>
        private FrameworkElement CreateLabel(PropertyInfo prop)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var accent = Application.Current?.TryFindResource("AccentBlue") as Brush ?? Brushes.DodgerBlue;
            var iconAttr = prop.GetCustomAttribute<IconAttribute>();

            if (!string.IsNullOrWhiteSpace(iconAttr?.IconCode))
            {
                try
                {
                    panel.Children.Add(new Path
                    {
                        Data = Geometry.Parse(iconAttr!.IconCode),
                        Fill = accent,
                        Width = 14,
                        Height = 14,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, _cardStyle ? 10 : 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                catch
                {
                    // 图标路径写错只影响这一行，不该让整个属性面板打不开
                    panel.Children.Add(new TextBlock { Text = "⚠ ", Foreground = Brushes.Red, VerticalAlignment = VerticalAlignment.Center });
                }
            }
            else
            {
                panel.Children.Add(new Border { Width = _cardStyle ? 24 : 22 });
            }

            var textBlock = new TextBlock { Text = _display?.Name ?? prop.Name };
            if (Application.Current?.TryFindResource("PropertyLabelStyle") is Style labelStyle)
            {
                textBlock.Style = labelStyle;
                // Description 优先于样式里"回显自身文本"的 ToolTip
                if (!string.IsNullOrWhiteSpace(_display?.Description))
                    textBlock.ToolTip = _display!.Description;
            }
            else
            {
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                textBlock.VerticalAlignment = VerticalAlignment.Center;
                textBlock.FontSize = 13;
            }

            panel.Children.Add(textBlock);

            if (Attribute.IsDefined(prop, typeof(RequiredAttribute)))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = " *",
                    Foreground = (Application.Current?.TryFindResource("FluentDangerBrush") as Brush)
                                 ?? new SolidColorBrush(Color.FromRgb(216, 59, 1)),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            return panel;
        }
    }
}
