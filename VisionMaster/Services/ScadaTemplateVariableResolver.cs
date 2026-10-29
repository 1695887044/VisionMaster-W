using System;
using System.Globalization;
using VisionMaster.Binding;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 模板展开用的变量解析器：把模板正文里的 <c>Control[3]</c> 解析成
    /// "100 个独立变量里的 Control[3]" 或 "数组变量 Control 的第 3 个元素"。
    ///
    /// 两种形态的优先级是<b>精确名优先</b>：
    /// 1. 先按完整文本查变量名（用户可能真的建了 100 个独立变量，名字就叫 <c>Control[0]</c>）；
    /// 2. 查不到再把最后一段 <c>[n]</c> 拆下来，按数组根 + 下标解析。
    /// 反过来（先拆数组）会让"独立变量恰好叫 Control[0]"的工程绑到另一个对象上，
    /// 而且这种错绑在界面上完全看不出来。
    /// </summary>
    public sealed class ScadaTemplateVariableResolver : IScadaTemplateVariableResolver
    {
        private readonly IVariableRegistry _registry;

        public ScadaTemplateVariableResolver(IVariableRegistry registry)
            => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        /// <inheritdoc />
        public bool TryResolve(string referenceText, out ScadaTemplateVariable variable, out string? error)
        {
            variable = default;
            error = null;

            string text = (referenceText ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                error = "变量引用为空";
                return false;
            }

            // ① 精确名优先：100 个独立变量的形态
            var exact = _registry.FindByName(text);

            if (exact != null)
            {
                if (exact.DataType.IsArray)
                {
                    error = $"变量「{text}」是数组，请指定元素下标（例如 {text}[0]）";
                    return false;
                }

                variable = FromVariable(exact, -1);
                return true;
            }

            // ①b 重名：精确名没命中，但这个名字确实存在（只是重复）。
            // 必须先于 ② 报出来——否则 `Alarm[90]` 会落到"数组根 Alarm 不存在"，
            // 把"同名两台设备"误报成"数组变量没建"，排查方向直接跑偏。
            if (VariableNameAmbiguity.TryExplain(_registry, text, out var ambiguity))
            {
                error = ambiguity;
                return false;
            }

            // ② 数组元素：Base[index]
            if (!TryParseElementReference(text, out string baseName, out int index))
            {
                error = $"找不到变量「{text}」";
                return false;
            }

            var root = _registry.FindByName(baseName);

            if (root == null)
            {
                if (VariableNameAmbiguity.TryExplain(_registry, baseName, out var rootAmbiguity))
                {
                    error = rootAmbiguity;
                    return false;
                }

                error = $"找不到变量「{text}」（数组根「{baseName}」不存在）";
                return false;
            }

            return TryResolveElement(root, index, text, out variable, out error);
        }

        /// <inheritdoc />
        public bool TryResolve(Guid variableId, string? variableName, int arrayIndex,
                               out ScadaTemplateVariable variable, out string? error)
        {
            variable = default;
            error = null;

            var root = _registry.Resolve(variableId, variableName);

            if (root == null)
            {
                string label = new ScadaVariableRef(variableId, variableName, arrayIndex).DisplayText;
                error = $"找不到变量「{label}」";
                return false;
            }

            if (arrayIndex < 0)
            {
                if (root.DataType.IsArray)
                {
                    error = $"变量「{root.Name}」是数组，请指定元素下标";
                    return false;
                }

                variable = FromVariable(root, -1);
                return true;
            }

            string reference = $"{root.Name}[{arrayIndex.ToString(CultureInfo.InvariantCulture)}]";
            return TryResolveElement(root, arrayIndex, reference, out variable, out error);
        }

        private static bool TryResolveElement(
            IVariable root,
            int index,
            string reference,
            out ScadaTemplateVariable variable,
            out string? error)
        {
            variable = default;
            error = null;

            if (!root.DataType.IsArray)
            {
                error = $"变量「{root.Name}」不是数组，不能按元素绑定";
                return false;
            }

            int length = GetArrayLength(root);

            if (length < 0)
            {
                error = $"变量「{root.Name}」的数组长度未知，无法按元素绑定";
                return false;
            }

            if (index < 0 || index >= length)
            {
                error = $"下标 {index} 超出数组「{root.Name}」的长度 {length}（引用「{reference}」）";
                return false;
            }

            Type elementType = root.DataType.GetElementType() ?? typeof(object);

            // 本地数组直接可写；网络数组必须真的能按元素地址下发（MC/FINS 没有元素地址）、
            // 不是只读区、且有地址配置。旧判据 `root is IArrayElementWritable` 对
            // NetworkVariableModel 恒真，会让不可写的网络数组在设计期也允许写动作，运行期才失败。
            bool writable = root is IArrayElementWritable
                && (root is not NetworkVariableModel network
                    || (network.SupportsElementWrite
                        && !network.IsReadOnlyArea
                        && network.AddressConfig != null));

            variable = new ScadaTemplateVariable(
                root.VariableId,
                root.Name ?? string.Empty,
                index,
                elementType,
                writable);

            return true;
        }

        private static ScadaTemplateVariable FromVariable(IVariable variable, int arrayIndex)
            => new(
                variable.VariableId,
                variable.Name ?? string.Empty,
                arrayIndex,
                variable.DataType,
                variable is IWritableVariable);

        private static int GetArrayLength(IVariable variable)
        {
            if (variable.Value is Array current)
                return current.Length;

            if (variable.DefaultValue is Array fallback)
                return fallback.Length;

            if (variable.AddressConfig is { Length: > 0 } address)
                return address.Length;

            return -1;
        }

        /// <summary>
        /// 解析 <c>Base[index]</c>：只认最后一个 <c>[</c> 且以 <c>]</c> 结尾的形态。
        /// 变量名本身可以带点、下划线、中文；下标必须是非负整数（不变区域性）。
        /// </summary>
        private static bool TryParseElementReference(string text, out string baseName, out int index)
        {
            baseName = string.Empty;
            index = -1;

            int open = text.LastIndexOf('[');

            if (open <= 0 || !text.EndsWith("]", StringComparison.Ordinal))
                return false;

            baseName = text[..open].Trim();

            if (baseName.Length == 0)
                return false;

            string indexText = text[(open + 1)..^1].Trim();

            return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                   && index >= 0;
        }
    }
}
