using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 权限目录的 15 条内置 Key（B3）。<b>Key 是落盘身份，显示名只用于上屏</b>；
    /// 改名只改显示名，Key 永不复用。
    ///
    /// <see cref="Always"/> 是系统级恒放行（登录 / 帮助 / 浏览），<b>不进目录</b>：
    /// 它没有"能不能勾"的语义，放进目录会让"32 条上限"与"内置不可删"两条规则互相打架。
    /// </summary>
    public static class ScadaPermissionKeys
    {
        public const string ViewRuntime = "ViewRuntime";
        public const string ControlRun = "ControlRun";
        public const string Start = "Start";
        public const string Initialize = "Initialize";
        public const string Stop = "Stop";
        public const string ReportQuery = "ReportQuery";
        public const string AxisJog = "AxisJog";
        public const string AxisSpeedConfig = "AxisSpeedConfig";
        public const string EditStructure = "EditStructure";
        public const string Compile = "Compile";
        public const string SaveSolution = "SaveSolution";
        public const string HardwareRuntime = "HardwareRuntime";
        public const string HardwareConfig = "HardwareConfig";
        public const string SystemConfig = "SystemConfig";
        public const string UserAdministration = "UserAdministration";

        /// <summary>系统级恒放行，不占目录条目、不可勾选、不可删除。</summary>
        public const string Always = "Always";

        /// <summary>
        /// 属性面板「不限制」的哨兵值。<b>只存在于界面</b>：写回模型前一律折成 <c>null</c>，
        /// 不落进 .vms。用带下划线的保留串而不是空串，是为了不和"下拉失配导致的空值"混为一谈
        /// （空值必须标红，不能静默当成放权）。
        /// </summary>
        public const string NotLimitedValue = "__none__";

        /// <summary>权限目录上限（含 15 条内置，自定义最多 17 条）。</summary>
        public const int MaxCatalogCount = 32;

        private static readonly ScadaPermissionInfo[] BuiltInArray =
        {
            new(ViewRuntime, "看运行画面", true),
            new(ControlRun, "运行控制", true),
            new(Start, "启动", true),
            new(Initialize, "初始化", true),
            new(Stop, "停止", true),
            new(ReportQuery, "报表查询", true),
            new(AxisJog, "轴点动", true),
            new(AxisSpeedConfig, "定位速度配置", true),
            new(EditStructure, "改结构", true),
            new(Compile, "编译", true),
            new(SaveSolution, "保存方案", true),
            new(HardwareRuntime, "硬件运行态", true),
            new(HardwareConfig, "硬件配置", true),
            new(SystemConfig, "系统配置", true),
            new(UserAdministration, "用户管理", true),
        };

        /// <summary>15 条内置权限（每次返回只读快照，调用方不能改内部数组）。</summary>
        public static IReadOnlyList<ScadaPermissionInfo> BuiltIn { get; } =
            Array.AsReadOnly(BuiltInArray);

        /// <summary>全部内置 Key（不含 <see cref="Always"/>）。</summary>
        public static IReadOnlyList<string> BuiltInKeys { get; } =
            Array.AsReadOnly(BuiltInArray.Select(p => p.Key).ToArray());

        /// <summary>显示名；未知 Key 回显原 Key（不抛异常，坏数据也要能画出来）。</summary>
        public static string DisplayName(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var found = BuiltInArray.FirstOrDefault(p =>
                string.Equals(p.Key, key, StringComparison.Ordinal));
            return found?.Name ?? key!;
        }

        /// <summary>是不是 15 条内置之一（<see cref="Always"/> 不算目录内置）。</summary>
        public static bool IsBuiltIn(string? key)
            => !string.IsNullOrWhiteSpace(key)
               && BuiltInArray.Any(p => string.Equals(p.Key, key, StringComparison.Ordinal));

        /// <summary>旧 .vms 的 RequiredRole 映射（历史数据兼容；新配置只写 RequiredPermission）。</summary>
        public static string LegacyRolePermission(ScadaRole role) => role switch
        {
            ScadaRole.Operator => ViewRuntime,
            ScadaRole.Engineer => ControlRun,
            _ => SystemConfig,
        };
    }

    /// <summary>运行态图元/画面权限判定的统一入口（旧 RequiredRole 只作兜底）。</summary>
    public static class ScadaPermissionPolicyExtensions
    {
        public static string? RequiredPermissionOf(this ScadaElement? element)
        {
            if (element == null) return null;
            if (!string.IsNullOrWhiteSpace(element.RequiredPermission)) return element.RequiredPermission;
            return element.RequiredRole is { } role ? ScadaPermissionKeys.LegacyRolePermission(role) : null;
        }

        public static string? RequiredPermissionOf(this ScadaPage? page)
        {
            if (page == null) return null;
            if (!string.IsNullOrWhiteSpace(page.RequiredPermission)) return page.RequiredPermission;
            return page.RequiredRole is { } role ? ScadaPermissionKeys.LegacyRolePermission(role) : null;
        }

        public static bool CanElement(this IScadaAccessPolicy policy, ScadaElement? element)
            => policy.CanElement(element, out _);

        public static bool CanElement(this IScadaAccessPolicy policy, ScadaElement? element, out string? reason)
        {
            var key = element.RequiredPermissionOf();
            if (string.IsNullOrWhiteSpace(key)) { reason = null; return true; }
            return policy.Can(key!, out reason);
        }

        public static bool CanPage(this IScadaAccessPolicy policy, ScadaPage? page)
            => policy.CanPage(page, out _);

        public static bool CanPage(this IScadaAccessPolicy policy, ScadaPage? page, out string? reason)
        {
            var key = page.RequiredPermissionOf();
            if (string.IsNullOrWhiteSpace(key)) { reason = null; return true; }
            return policy.Can(key!, out reason);
        }
    }
}
