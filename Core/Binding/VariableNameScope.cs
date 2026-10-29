using System;
using System.Collections.Generic;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 变量名的**作用域**判定：同名是否构成冲突。
    ///
    /// 口径（2026-10-29 起）：**同一设备内变量名唯一**，不同设备允许同名。
    /// - 网络变量的作用域 = 它的 <see cref="NetworkVariableModel.ConnectionName"/>；
    ///   连接名比较用 <see cref="StringComparison.Ordinal"/>（与轮询注册表的键一致，
    ///   <c>AdvancedCommunicationManager</c> 的字典就是序数比较，别用忽略大小写去"宽容"）。
    /// - 本地变量 / 系统变量**没有设备**：作用域视为"全局"，与任何变量同名都算冲突。
    ///   它们一旦与某台设备的点位同名，按名解析就无法确定目标（<c>FindByName</c> 返回 null），
    ///   所以不能因为"设备不同"就放行。
    ///
    /// 三处判定共用本类，避免"导入放行、新建拦住"这种自相矛盾：
    /// 导入 <c>DeviceVariableImportService.BuildPlan</c>、新建
    /// <c>GlobalVariableManagerViewModel.AddVariableCore</c>、改名
    /// <c>VariableRegistry.TryRename</c>。
    /// </summary>
    public static class VariableNameScope
    {
        /// <summary>
        /// 目标作用域（要写入/改成的那一侧）与一个既有变量是否同名冲突。
        /// </summary>
        /// <param name="scopeConnectionName">
        /// 目标所属连接名；<c>null</c>/空 = 目标是本地变量（全方案唯一）。
        /// </param>
        /// <param name="other">既有变量（名字是否相同由调用方判断，本方法只看作用域）</param>
        public static bool Conflicts(string? scopeConnectionName, IVariable? other)
        {
            if (other == null)
                return false;

            // 目标没有设备（本地变量）→ 与任何变量都算冲突
            if (string.IsNullOrEmpty(scopeConnectionName))
                return true;

            // 对方没有设备（本地/系统变量）→ 它占的是"全方案唯一"的名字，目标也得让路。
            // 漏这一支的后果是：本地变量叫 Alarm[90]，某台设备的点位也能改叫 Alarm[90]，
            // 之后按名解析永远歧义——而改名那一刻是"成功"的，用户不会知道。
            if (other is not NetworkVariableModel network)
                return true;

            // 双方都在设备上 → 只有同一连接才冲突
            return string.Equals(
                Normalize(network.ConnectionName), Normalize(scopeConnectionName),
                StringComparison.Ordinal);
        }

        /// <summary>两个既有变量之间是否同名冲突（改名用：被改的那个当作"目标作用域"）</summary>
        public static bool Conflicts(IVariable? target, IVariable? other)
        {
            if (target == null || other == null)
                return false;

            // 只有"两边都是网络变量"才谈得上不同设备；任一侧是本地/系统变量 → 全方案唯一。
            // 必须对称：Conflicts(x, y) == Conflicts(y, x)（探针 C10.3 钉这一点）。
            if (target is NetworkVariableModel targetNetwork && other is NetworkVariableModel otherNetwork)
            {
                return string.Equals(
                    Normalize(targetNetwork.ConnectionName), Normalize(otherNetwork.ConnectionName),
                    StringComparison.Ordinal);
            }

            return true;
        }

        /// <summary>变量的作用域连接名（本地/系统变量返回 null）</summary>
        public static string? ScopeOf(IVariable? variable)
            => variable is NetworkVariableModel network ? network.ConnectionName : null;

        /// <summary>
        /// 在一批变量里找出"与目标作用域同名冲突"的第一个变量；返回 null = 可以创建。
        ///
        /// 新建变量与导入的共用入口。名字比较**大小写不敏感**（与变量管理查重、监视栏匹配同一口径），
        /// 作用域判定交给 <see cref="Conflicts(string?, IVariable?)"/>。
        /// 抽成静态方法而不是让每个调用方各写一遍 LINQ：三处判定只要有一处写成"名字相同就冲突"，
        /// 就会出现"导入放行、新建拦住"这种自相矛盾，而两边都不会报错。
        /// </summary>
        public static IVariable? FindBlocker(
            IEnumerable<IVariable>? variables, string? name, string? scopeConnectionName)
        {
            var key = (name ?? string.Empty).Trim();
            if (variables == null || key.Length == 0)
                return null;

            foreach (var variable in variables)
            {
                if (variable == null || string.IsNullOrEmpty(variable.Name))
                    continue;
                if (!string.Equals(variable.Name.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Conflicts(scopeConnectionName, variable))
                    continue;

                return variable;
            }

            return null;
        }

        /// <summary>上屏用的作用域描述：<c>设备「L1」</c> / <c>本地变量</c> / <c>系统变量</c></summary>
        public static string DescribeScope(IVariable? variable)
        {
            if (variable == null)
                return "（未知变量）";

            if (variable is NetworkVariableModel network)
            {
                return string.IsNullOrWhiteSpace(network.ConnectionName)
                    ? "未指定连接的通讯变量"
                    : $"设备「{network.ConnectionName}」";
            }

            return variable.VariableType switch
            {
                VariableType.Local => "本地变量",
                VariableType.System => "系统变量",
                VariableType.Io => "IO 变量",
                _ => "本地变量",
            };
        }

        private static string Normalize(string? connectionName) => (connectionName ?? string.Empty).Trim();
    }
}
