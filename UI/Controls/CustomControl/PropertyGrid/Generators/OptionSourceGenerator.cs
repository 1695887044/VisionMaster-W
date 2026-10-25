using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Core.Interfaces;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 动态候选下拉：给标了 <see cref="StepConfigOptionsAttribute"/> 的 string 参数生成 ComboBox。
    ///
    /// 与 <see cref="EnumGenerator"/> 的分工：
    ///   · EnumGenerator 处理**编译期已知**的枚举（候选固定，显示名取 [Description]）；
    ///   · 本生成器处理**运行期才知道候选**的参数 —— 轴名/卡地址这类
    ///     要跟着当前方案变化的（用户改了轴映射表，候选就该变）。
    ///
    /// 【2026-10-24 口径变更（用户裁决）：不再允许手填】
    /// 旧实现是 <c>IsEditable = true</c>，理由是"候选为空是常态，只读下拉连字都打不进去"。
    /// 实测那是**静默缺陷**：轴名/卡名是框架的权威清单，手打一个不存在的轴名界面毫无提示，
    /// 要到运行期才炸；而且"宿主漏注册来源"（程序缺陷）与"还没配卡"（正常）长得一模一样。
    /// 现口径：**只选不填**，候选取不到时给出原因（缺陷态红字），
    /// 与 <c>LinkableValueEditor</c> 的空态保持同一套判据（都走 GetOptionsResult）。
    /// </summary>
    public class OptionSourceGenerator : IControlGenerator
    {
        // 与 EnumGenerator 同级：两者 CanProcess 互斥（一个要求是枚举、一个要求不是枚举），
        // 谁先谁后都一样；比 TypeGenerator（兜底）必须靠前，否则轮不到它。
        public int Priority => 100;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly)
            => !targetType.IsEnum
               && prop.GetCustomAttribute<StepConfigOptionsAttribute>() != null;

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var attribute = prop.GetCustomAttribute<StepConfigOptionsAttribute>()!;
            var result = StepConfigOptionSource.GetOptionsResult(attribute.Kind);
            var options = result.Items;

            var comboBox = new ComboBox
            {
                IsEnabled = !isReadOnly && options.Count > 0,
                // ★ 不可编辑：值只能从候选里选（理由见类注释的 2026-10-24 口径）。
                IsEditable = false,
                ItemsSource = options,
                ToolTip = options.Count > 0
                    ? $"候选来自当前方案（共 {options.Count} 项）；只能从候选中选择"
                    : DescribeEmptyReason(attribute.Kind, result),
            };

            // 空候选时给一行明确的红字/灰字说明：下拉是灰的，不说明原因等于"看起来坏了"。
            if (options.Count == 0)
                comboBox.Style = BuildEmptyStyle(result.IsDefect);

            // v1 基线：统一 36px 高 / 8px 圆角 / 令牌配色
            PropertyGridMetrics.ApplyInputLook(comboBox);

            // 绑到属性本身（TwoWay）。不可编辑时 Text 与 SelectedValue 等价，
            // 但走 SelectedValue 更准确：它只在真选中候选时才写回，不会带入任何手打文本。
            var binding = new Binding(prop.Name)
            {
                Source = bindingSource,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            };
            comboBox.SetBinding(Selector.SelectedValueProperty, binding);

            return comboBox;
        }

        /// <summary>空候选原因文案：程序缺陷（红）与正常未配（灰）必须能分开。</summary>
        private static string DescribeEmptyReason(StepConfigOptionKind kind, StepConfigOptionResult result)
            => result.Failure switch
            {
                StepConfigOptionFailureKind.NoProvider
                    => $"程序缺陷：宿主没有为「{kind}」登记候选来源，请把这个提示反馈给开发。",
                StepConfigOptionFailureKind.ProviderThrew
                    => $"程序缺陷：取「{kind}」候选时出错 —— {result.ProviderErrors[0]}。请反馈给开发。",
                _ => $"当前方案里还没有可用的「{kind}」：先到对应的设备设置里配好，再回到这里选择。",
            };

        /// <summary>空候选的视觉：灰底禁用样式；缺陷态再叠一层红色描边（由下面模板里的 Tag 触发）。</summary>
        private static Style BuildEmptyStyle(bool isDefect)
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.OpacityProperty, 1.0));

            var template = new ControlTemplate(typeof(ComboBox));
            var border = new FrameworkElementFactory(typeof(Border), "EmptyBd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, isDefect
                ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE5, 0x7A, 0x7A))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCB, 0xD5, 0xE1)));
            border.SetValue(Border.BackgroundProperty, isDefect
                ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFE, 0xF2, 0xF2))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC)));

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 10, 0));
            text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(TextBlock.FontSizeProperty, 12.0);
            text.SetValue(TextBlock.ForegroundProperty, isDefect
                ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xC0, 0x2B, 0x2B))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)));
            text.SetValue(TextBlock.TextProperty, "当前没有可选值");
            border.AppendChild(text);

            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
    }
}
