using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 「候选来自绑定对象自己」的下拉：给标了 <see cref="ComboSourceAttribute"/> 的 string 属性生成 ComboBox，
    /// 候选取 <c>SourceProperty</c> 指向的那个属性值。
    ///
    /// 与两位邻居的分工：
    ///   · <see cref="EnumGenerator"/> 处理**编译期已知**的枚举（候选固定，显示名取 [Description]）；
    ///   · <see cref="OptionSourceGenerator"/> 处理**宿主全局注册**的候选（轴名/卡地址），
    ///     用 <c>StepConfigOptionKind</c> 表达"去哪个来源取"——静态 kind，够不到"挂在对象上的候选"；
    ///   · 本生成器处理**绑定对象自带的**候选：弹窗草稿（DTO）在构造时由调用方注入
    ///     连接名 / 扫描组 / 存储区这类列表，它们既不是编译期常量、也不在宿主注册表里。
    ///
    /// 【Priority = 105 的理由】必须**高于** <see cref="StructValueGenerator"/>（100）——
    /// 后者用文本框渲染一切 string，抢在它前面才有下拉可言（低于 NumericGenerator 的 110 无妨：
    /// 本特性只对 string 有意义，数值属性不会同时命中两者）。
    ///
    /// 【绝不抛异常】候选属性取不到（没这个属性 / 值为 null / 取值抛）一律退成空候选：
    /// 属性面板可能被"没有合并主题的宿主"或设计器 new 出来，那里出异常是整个面板白屏，
    /// 而"下拉是空的"只是可选项少，用户仍能继续编辑别的字段。
    /// </summary>
    public class ComboSourceGenerator : IControlGenerator
    {
        public int Priority => 105;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly)
            => !targetType.IsEnum
               && prop.GetCustomAttribute<ComboSourceAttribute>() != null;

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var attribute = prop.GetCustomAttribute<ComboSourceAttribute>()!;
            var options = ResolveOptions(bindingSource, attribute.SourceProperty);

            var comboBox = new ComboBox
            {
                IsEnabled = !isReadOnly,
                IsEditable = attribute.IsEditable,
                ItemsSource = options,
            };
            // v1 基线：统一 36px 高 / 8px 圆角 / 令牌配色（原来写死 Padding=10,6 与裸边框）
            PropertyGridMetrics.ApplyInputLook(comboBox);

            var binding = new Binding(prop.Name)
            {
                Source = bindingSource,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            };

            if (attribute.IsEditable)
            {
                // ★ 可编辑时绑 Text 而不是 SelectedItem：只有 Text 会带上"用户手打的字"，
                //   绑 SelectedItem 的话手输不写回属性，等于把"允许手填"做成摆设
                //   （与 OptionSourceGenerator 同款判据，那里有完整说明）。
                comboBox.SetBinding(ComboBox.TextProperty, binding);
            }
            else
            {
                comboBox.SetBinding(Selector.SelectedItemProperty, binding);
            }

            return comboBox;
        }

        /// <summary>
        /// 取候选：优先按 <c>IEnumerable&lt;string&gt;</c>，否则把任意集合逐项 ToString。
        /// 取不到就给空列表（见类型注释：这里不许抛）。
        /// </summary>
        private static IReadOnlyList<string> ResolveOptions(object bindingSource, string sourceProperty)
        {
            try
            {
                var source = bindingSource?
                    .GetType()
                    .GetProperty(sourceProperty, BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(bindingSource);

                return source switch
                {
                    null => Array.Empty<string>(),
                    IEnumerable<string> texts => texts.ToList(),
                    IEnumerable items => items.Cast<object?>().Select(o => o?.ToString() ?? string.Empty).ToList(),
                    _ => Array.Empty<string>(),
                };
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
