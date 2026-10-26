using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Scada
{
    /// <summary>用户组。B3：组是权限的唯一载体，账号只记 <see cref="Id"/>。</summary>
    public sealed record ScadaGroupInfo(
        string Id,
        string Name,
        IReadOnlyList<string> Permissions,
        bool IsBuiltIn);

    /// <summary>权限目录里的一条。内置条目的 <see cref="Key"/> 是稳定契约，显示名可改。</summary>
    public sealed record ScadaPermissionInfo(string Key, string Name, bool IsBuiltIn);

    /// <summary>
    /// 默认组种子：3 个内置模板 + 3 个自定义示例。
    ///
    /// 模板是"新建组时抄一份"的起点（只读，不可改名/改权限/删除）；自定义示例让现场
    /// 打开用户管理页就能看到"普工 / 产线操作员 / 厂家"三种典型配置，而不是面对空列表。
    /// 两者都只在内存里种，首次保存方案时才随三文件一起落盘。
    /// </summary>
    public static class ScadaGroupDefaults
    {
        public const string OperatorTemplateId = "tpl-operator";
        public const string EngineerTemplateId = "tpl-engineer";
        public const string AdminTemplateId = "tpl-admin";

        public static IReadOnlyList<ScadaGroupInfo> CreateDefaultTemplates()
        {
            var operatorPermissions = new[]
            {
                ScadaPermissionKeys.ViewRuntime,
                ScadaPermissionKeys.ControlRun,
                ScadaPermissionKeys.Start,
                ScadaPermissionKeys.Initialize,
                ScadaPermissionKeys.Stop,
                ScadaPermissionKeys.ReportQuery,
                ScadaPermissionKeys.AxisJog,
            };

            var engineerPermissions = operatorPermissions.Concat(new[]
            {
                ScadaPermissionKeys.EditStructure,
                ScadaPermissionKeys.Compile,
                ScadaPermissionKeys.SaveSolution,
                ScadaPermissionKeys.HardwareRuntime,
                ScadaPermissionKeys.AxisSpeedConfig,
            }).ToArray();

            var adminPermissions = ScadaPermissionKeys.BuiltInKeys.ToArray();

            return new[]
            {
                new ScadaGroupInfo(OperatorTemplateId, "模板·操作员", operatorPermissions, true),
                new ScadaGroupInfo(EngineerTemplateId, "模板·工程师", engineerPermissions, true),
                new ScadaGroupInfo(AdminTemplateId, "模板·管理员", adminPermissions, true),
                new ScadaGroupInfo("G-general", "普工", new[]
                {
                    ScadaPermissionKeys.ViewRuntime,
                    ScadaPermissionKeys.Start,
                    ScadaPermissionKeys.Stop,
                }, false),
                new ScadaGroupInfo("G-lineop", "产线操作员", operatorPermissions, false),
                new ScadaGroupInfo("G-vendor", "厂家", adminPermissions, false),
            };
        }
    }
}
