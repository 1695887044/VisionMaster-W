using System;
using System.Globalization;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条变量引用的完整身份：变量根 + 可选的数组元素下标。
    ///
    /// 为什么下标不塞进 <see cref="VariableName"/>：名字是给人看的展示串、也是旧数据
    /// 按名兜底的键，一旦把 "Control[3]" 当名字存，改名级联、选择器预选、精确名优先
    /// 三条规则都会各解一遍这个字符串，迟早分叉。下标是引用的结构字段，必须显式。
    ///
    /// <see cref="ArrayIndex"/> = -1 表示引用整个变量（历史行为）；≥ 0 表示引用该数组
    /// 变量的一个元素。SCADA 领域层只承载这个事实，不解释"数组怎么取元素"——
    /// 那是值源（VM.Core 的 RegistryScadaValueSource）的职责。
    /// </summary>
    public readonly record struct ScadaVariableRef(Guid VariableId, string? VariableName, int ArrayIndex = -1)
    {
        /// <summary>是否引用数组元素</summary>
        public bool IsArrayElement => ArrayIndex >= 0;

        /// <summary>
        /// 展示/诊断用的文本：<c>Control</c> 或 <c>Control[3]</c>。
        /// 名字为空时退回 <c>(未指定)</c>，不留空白——空白看不出是"没配"还是"配了但名字丢了"。
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(VariableName))
                    return "(未指定)";

                return IsArrayElement
                    ? $"{VariableName}[{ArrayIndex.ToString(CultureInfo.InvariantCulture)}]"
                    : VariableName!;
            }
        }
    }
}
