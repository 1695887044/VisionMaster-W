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
    /// （扁平：标签列 + 值列，行间 1px 分隔线；卡片：标签 + 控件，12 栅格）。
    /// 分成两个类之后，"补一个必填红星""换个主题色"都要改两处。
    /// 这里用 <see cref="_cardStyle"/> 参数化皮肤，逻辑只有一份。
    ///
    /// 【v1 设计基线】docs\UI设计基线\property-grid\design.html
    ///   · 行高 48px（MinHeight），标签/值两侧同竖向内边距（10px），控件高 36px
    ///   · 标签列 150px（扁平式），去掉原来 32px 的右内边距
    ///   · 悬停整行浅底；最后一行不带底边线（由外框收口）
    ///   · 颜色一律 TryFindResource + 令牌，取不到回退中性灰（宿主可能没合并本库主题）
    /// </summary>
    public class PropertyGridLayoutProcessor : IControlProcessor
    {
        /// <summary>扁平式标签列宽（基线 150px）</summary>
        private const double FlatLabelWidth = 150;

        /// <summary>行最小高度（基线 48px = 36px 控件 + 上下 6px）</summary>
        private const double RowMinHeight = 48;

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
            //
            // Button 同理贴左：命令按钮（[Command] 渲染的"执行"）被 Stretch 会拉成一整条
            // 主色横杠，看起来像"整行都是可点的"（首轮出图就是这样）。
            context.Control.HorizontalAlignment = context.Control switch
            {
                System.Windows.Controls.Primitives.ToggleButton => HorizontalAlignment.Left,
                Button => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Stretch,
            };
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

            // ---------- 扁平式 ----------
            // 整行一条底部分隔线，标签列与值列**同底色**，层级只靠 1px 分隔线 ——
            // 原来左灰右白两块底色硬切，看着割裂；统一底色后"这是两组格"的信息交给分隔线。
            var borderColor = Brush("DialogBorderLightBrush", 238, 242, 247);
            var hoverColor = Brush("DialogRowAlternateBrush", 248, 250, 252);

            var rowBorder = new Border
            {
                BorderBrush = borderColor,
                // 最后一行不带底边线：外框已有 1px 描边，再叠一条会压在圆角内侧
                BorderThickness = context.IsLastRow ? new Thickness(0) : new Thickness(0, 0, 0, 1),
                MinHeight = RowMinHeight,
                // 刻意给透明底：WPF 里 Background=null 的元素**不参与命中测试**，
                // 不给透明底的话"点行内空白处切换开关"就是一句空话
                Background = Brushes.Transparent,
            };

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = context.SharedLabelGroup });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 标签列：150px 定宽（基线），两侧同竖向内边距
            var labelBorder = new Border
            {
                Width = FlatLabelWidth,
                Padding = new Thickness(14, 10, 14, 10),
                Child = label,
            };
            Grid.SetColumn(labelBorder, 0);

            // 值列：上下 5.5px（36px 控件 + 11 = 47，加底部 1px 分隔线正好 48px 行高）。
            // 用 5.5 而不是 6：Border 的 BorderThickness 是占布局的，写 6 会让内容区变 48、
            // 加边框后整行 49，与「行高统一 48」差 1px —— 这种"差一像素"靠肉眼看不出来，
            // 只有断言能抓（首轮实测就抓到了 48/49 混排）。
            var controlBorder = new Border { Padding = new Thickness(14, 5.5, 14, 5.5), Child = wrapper };
            Grid.SetColumn(controlBorder, 1);

            innerGrid.Children.Add(labelBorder);
            innerGrid.Children.Add(controlBorder);
            rowBorder.Child = innerGrid;
            grid.Children.Add(rowBorder);

            // 悬停整行浅底：用 Style 触发器而不是 MouseEnter 事件，
            // 免去手工解绑（重绘时旧行被事件链拽住是内存泄漏的常见来源）。
            var hoverStyle = new Style(typeof(Border));
            hoverStyle.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverColor));
            hoverStyle.Triggers.Add(hoverTrigger);
            rowBorder.Style = hoverStyle;

            // 整行可点：点行内任意位置（含「启用」文字）都能切换，热区不再只有那一小颗
            MakeRowClickable(rowBorder, toggle);
        }

        /// <summary>取令牌画刷；取不到回退中性灰（不抛——宿主可能没合并本库主题）</summary>
        private static Brush Brush(string key, byte r, byte g, byte b)
            => Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush(Color.FromRgb(r, g, b));

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

            var accent = Application.Current?.TryFindResource("DialogAccentBrush") as Brush ?? Brushes.DodgerBlue;
            var iconAttr = prop.GetCustomAttribute<IconAttribute>();

            if (!string.IsNullOrWhiteSpace(iconAttr?.IconCode))
            {
                try
                {
                    panel.Children.Add(new Path
                    {
                        Data = Geometry.Parse(iconAttr!.IconCode),
                        Fill = accent,
                        Width = 12,
                        Height = 12,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                catch
                {
                    // 图标路径写错只影响这一行，不该让整个属性面板打不开
                    panel.Children.Add(new TextBlock
                    {
                        Text = "⚠",
                        Foreground = Brush("DialogDangerBrush", 239, 68, 68),
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
            }

            var textBlock = new TextBlock
            {
                Text = _display?.Name ?? prop.Name,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = Brush("DialogTextSecondaryBrush", 71, 85, 105),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            if (Application.Current?.TryFindResource("PropertyLabelStyle") is Style labelStyle)
                textBlock.Style = labelStyle;

            // Description 优先于样式里"回显自身文本"的 ToolTip
            if (!string.IsNullOrWhiteSpace(_display?.Description))
                textBlock.ToolTip = _display!.Description;

            panel.Children.Add(textBlock);

            if (Attribute.IsDefined(prop, typeof(RequiredAttribute)))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "*",
                    Foreground = Brush("DialogDangerBrush", 239, 68, 68),
                    FontSize = 11,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            return panel;
        }
    }
}
