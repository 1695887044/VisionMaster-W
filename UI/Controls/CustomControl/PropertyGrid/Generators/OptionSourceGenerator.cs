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
    /// **可编辑是刻意的**（<c>IsEditable = true</c>）：
    /// 候选为空是常态（还没配卡、还没连卡、临时想试个名字），
    /// 若做成只读下拉，这些情况下用户连字都打不进去 —— 那是"更不直观"。
    /// 下拉的价值是"把常见答案摆在眼前"，不是"禁止其它答案"。
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
            var options = StepConfigOptionSource.GetOptions(attribute.Kind);

            var comboBox = new ComboBox
            {
                IsEnabled = !isReadOnly,
                IsEditable = true,
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = options,
                ToolTip = options.Count > 0
                    ? $"候选来自当前方案（共 {options.Count} 项）；也可以直接输入"
                    : "当前方案里还没有可用项：可先到「运动卡设置」配置，或直接手动输入",
            };

            // 绑到属性本身（TwoWay）
            var binding = new Binding(prop.Name)
            {
                Source = bindingSource,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            };

            // ★ 绑 **Text** 而不是 SelectedValue：IsEditable = true 时，
            //   只有 Text 会带上"用户手打的字"；绑 SelectedValue 的话手动输入**不会写回**属性，
            //   等于把上面那句"允许手填"做成摆设（下拉选了才生效）。
            comboBox.SetBinding(ComboBox.TextProperty, binding);

            return comboBox;
        }
    }
}
