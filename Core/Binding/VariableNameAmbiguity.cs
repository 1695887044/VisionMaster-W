using System.Collections.Generic;
using System.Text;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// "按名寻址遇到重名"时的统一说明文案。
    ///
    /// 为什么要有这一层：<see cref="IVariableRegistry.FindByName"/> 在名字重复时返回 null，
    /// 各调用点若各自写一句"找不到变量"，用户会以为是变量被删了——排查方向直接跑偏。
    /// 重名与"不存在"是两种完全不同的故障，必须分开说，并且说清**重复在哪几台设备上**。
    ///
    /// 口径：只解释"为什么按名找不到"，不替调用方决定是否回退（例如监视栏仍会落一条失效项）。
    /// </summary>
    public static class VariableNameAmbiguity
    {
        /// <summary>
        /// 若 <paramref name="name"/> 命中多个变量，产出可直接上屏的歧义说明并返回 true；
        /// 唯一命中、无命中、空名字都返回 false（把"不存在"留给调用方原有文案）。
        /// </summary>
        public static bool TryExplain(IVariableRegistry? registry, string? name, out string? error)
        {
            error = null;
            if (registry == null)
                return false;

            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
                return false;

            var matches = registry.FindAllByName(key);
            if (matches.Count <= 1)
                return false;

            error = Describe(key, matches);
            return true;
        }

        /// <summary>把"哪些变量重名"拼成一句话（设备名优先，其次变量种类）</summary>
        public static string Describe(string name, IReadOnlyList<IVariable> matches)
        {
            var sources = new StringBuilder();
            for (var i = 0; i < matches.Count; i++)
            {
                if (i > 0)
                    sources.Append(" / ");
                sources.Append(DescribeSource(matches[i]));
            }

            return $"变量名「{name}」在 {matches.Count} 个变量上重复（{sources}），"
                   + "无法按名确定目标；请改用变量绑定（按变量身份寻址），或给变量改名区分";
        }

        private static string DescribeSource(IVariable? variable)
        {
            if (variable == null)
                return "（未知变量）";

            if (variable is NetworkVariableModel network && !string.IsNullOrWhiteSpace(network.ConnectionName))
                return $"设备 {network.ConnectionName}";

            return variable.VariableType switch
            {
                VariableType.Local => "本地变量",
                VariableType.System => "系统变量",
                VariableType.Communication => "通讯变量",
                VariableType.Io => "IO 变量",
                _ => variable.VariableType.ToString(),
            };
        }
    }
}
