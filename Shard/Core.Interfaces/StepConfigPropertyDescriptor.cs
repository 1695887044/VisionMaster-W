using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// [StepConfig] 配置属性的**只读描述**（给"没有自定义配置视图"的通用参数面板渲染用）。
    ///
    /// 【为什么需要它】插件基类早就把标了 <see cref="StepConfigAttribute"/> 的属性发现出来了，
    /// 但那份清单（<c>VisionPluginBase._configProps</c>）是私有的、只能用于灌值/写回。
    /// 没有自定义视图的插件因此只有输入端口能编辑 —— 标成 [StepConfig] 的参数
    /// （「轴回零」的回零方式与超时、「轴停止」的停止方式等）在界面上**没有任何入口**。
    /// 本类把那份私有清单投影成一份只读快照，面板照着它逐行渲染即可。
    ///
    /// 【契约口径与 OnConfirm 逐字一致】<see cref="Name"/> 就是属性名，也就是 InputValues 的键。
    /// 这里**不做**任何改名/迁移：面板写回插件实例，确认时仍由
    /// <see cref="VisionPluginBase.OnConfirm"/> 按属性名落到 InputValues。
    ///
    /// 【为什么 GetValue/SetValue 是委托而不是反射 PropertyInfo】实例是长生命周期的配置对象，
    /// 委托在发现时闭包住 PropertyInfo，渲染期每次取值不必再做一次反射查找；
    /// 同时把"读/写只作用于发现它的那个实例"这件事钉在类型里。
    /// </summary>
    public sealed class StepConfigPropertyDescriptor
    {
        private readonly Func<object?> _getValue;
        private readonly Action<object?> _setValue;

        internal StepConfigPropertyDescriptor(
            string name,
            string? displayName,
            string? description,
            Type type,
            Func<object?> getValue,
            Action<object?> setValue,
            StepConfigOptionKind? optionsKind = null)
        {
            Name = name;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName!;
            Description = description ?? string.Empty;
            Type = type;
            _getValue = getValue;
            _setValue = setValue;
            OptionsKind = optionsKind;

            IsBoolean = type == typeof(bool) || type == typeof(bool?);

            // 枚举候选走 ForEnum：显示名取成员上的 [Display(Name)]，值仍是成员名
            //（R40：显示名可以随便改，写进流程的必须是稳定键）。
            if (type.IsEnum)
            {
                Options = Enum.GetNames(type)
                    .Select(member => StepConfigOption.ForEnum(type, member))
                    .ToArray();
            }
        }

        /// <summary>属性名 —— 同时是 InputValues 的键（与 OnConfirm 的写回键逐字一致）</summary>
        public string Name { get; }

        /// <summary>界面显示名：优先属性上的 [Display(Name)]，没标回退属性名（绝不回退成空白）</summary>
        public string DisplayName { get; }

        /// <summary>说明文字：[Display(Description)]；没标为空串</summary>
        public string Description { get; }

        /// <summary>属性声明类型（枚举/数值/字符串/bool 的渲染判据）</summary>
        public Type Type { get; }

        /// <summary>枚举候选（仅枚举类型非 null）；值 = 成员名，显示 = 中文名</summary>
        public IReadOnlyList<StepConfigOption>? Options { get; }

        /// <summary>
        /// 运行期候选来源（仅标了 <see cref="StepConfigOptionsAttribute"/> 的非枚举参数非 null）。
        ///
        /// 【为什么放在描述符上】此前只有 PropertyGrid 的 OptionSourceGenerator 会读这个特性，
        /// 而 **AutoPortConfigView（框架通用参数面板）读不到** —— 于是同一份 [StepConfig] 属性
        /// 在属性面板里是下拉、在步骤参数面板里却退化成可自由输入的文本框（v3 修的就是这个分叉）。
        /// 候选本身**不在这里取**：它随方案变化，必须在渲染/刷新那一刻现取（见 StepConfigOptionSource），
        /// 所以这里只携带"类别"，不携带列表。
        /// </summary>
        public StepConfigOptionKind? OptionsKind { get; }

        /// <summary>是否布尔（面板渲染成 CheckBox）</summary>
        public bool IsBoolean { get; }

        /// <summary>读取插件实例上的当前值</summary>
        public object? GetValue() => _getValue();

        /// <summary>
        /// 写回插件实例（面板是"编辑即时生效"：值先进插件实例，确认时由 OnConfirm 统一落 InputValues）。
        /// 类型不匹配时先走 <see cref="ValueConverter.Convert"/> 宽容转换；转不了就抛，
        /// 由面板显示红框——**绝不静默写 0 / 静默丢弃**。
        /// </summary>
        public void SetValue(object? value)
        {
            if (value != null && !Type.IsInstanceOfType(value))
                value = ValueConverter.Convert(value, Type);

            _setValue(value);
        }
    }
}
