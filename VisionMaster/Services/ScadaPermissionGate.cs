using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 动作 → 权限 Key 的唯一一份映射，外加「当前组是否含这个 Key」的唯一一个判定入口。
    /// 四类入口（Shell 菜单 / 流程栏 / 运行画面图元 / 命令面板）都从这里取，避免四份事实源。
    /// </summary>
    public sealed class ScadaPermissionGate
    {
        private readonly ScadaAccessPolicy _accessPolicy;

        public ScadaPermissionGate(ScadaAccessPolicy accessPolicy)
            => _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));

        public bool Can(string requiredKey, out string? reason)
        {
            if (string.IsNullOrWhiteSpace(requiredKey))
            {
                reason = "未登记的权限 Key（空值）";
                return false;
            }

            if (_accessPolicy.Can(requiredKey, out reason))
                return true;

            reason ??= $"需要「{ScadaPermissionKeys.DisplayName(requiredKey)}」权限";
            return false;
        }

        public static string RequiredPermission(SolutionAction action) => action switch
        {
            SolutionAction.Create => ScadaPermissionKeys.EditStructure,
            SolutionAction.Open => ScadaPermissionKeys.EditStructure,
            SolutionAction.BrowseList => ScadaPermissionKeys.EditStructure,
            SolutionAction.AlarmConfig => ScadaPermissionKeys.EditStructure,
            SolutionAction.Save => ScadaPermissionKeys.SaveSolution,
            SolutionAction.SaveAs => ScadaPermissionKeys.SaveSolution,
        };

        public static bool TryRequiredPermission(SolutionAction action, out string key)
        {
            switch (action)
            {
                case SolutionAction.Create:
                case SolutionAction.Open:
                case SolutionAction.BrowseList:
                case SolutionAction.AlarmConfig: key = ScadaPermissionKeys.EditStructure; return true;
                case SolutionAction.Save:
                case SolutionAction.SaveAs: key = ScadaPermissionKeys.SaveSolution; return true;
                default: key = string.Empty; return false;
            }
        }

        public static string RequiredPermission(ExecutionAction action) => action switch
        {
            ExecutionAction.Compile => ScadaPermissionKeys.Compile,
            ExecutionAction.RunOnce => ScadaPermissionKeys.ControlRun,
            ExecutionAction.RunContinuous => ScadaPermissionKeys.ControlRun,
            ExecutionAction.Pause => ScadaPermissionKeys.ControlRun,
            ExecutionAction.Resume => ScadaPermissionKeys.ControlRun,
            ExecutionAction.Stop => ScadaPermissionKeys.Stop,
            ExecutionAction.StepOnce => ScadaPermissionKeys.EditStructure,
        };

        public static bool TryRequiredPermission(ExecutionAction action, out string key)
        {
            switch (action)
            {
                case ExecutionAction.Compile: key = ScadaPermissionKeys.Compile; return true;
                case ExecutionAction.RunOnce:
                case ExecutionAction.RunContinuous:
                case ExecutionAction.Pause:
                case ExecutionAction.Resume: key = ScadaPermissionKeys.ControlRun; return true;
                case ExecutionAction.Stop: key = ScadaPermissionKeys.Stop; return true;
                case ExecutionAction.StepOnce: key = ScadaPermissionKeys.EditStructure; return true;
                default: key = string.Empty; return false;
            }
        }

        public static string RequiredPermission(SystemAction action) => action switch
        {
            SystemAction.UserLogin => ScadaPermissionKeys.Always,
            SystemAction.Help => ScadaPermissionKeys.Always,
            SystemAction.ReportQuery => ScadaPermissionKeys.ReportQuery,
            SystemAction.ReturnToZero => ScadaPermissionKeys.HardwareRuntime,
            SystemAction.MotionDebug => ScadaPermissionKeys.HardwareRuntime,
            SystemAction.MotionBoard => ScadaPermissionKeys.HardwareRuntime,
            SystemAction.AxisPoints => ScadaPermissionKeys.HardwareRuntime,
            SystemAction.GlobalVariables => ScadaPermissionKeys.EditStructure,
            SystemAction.VariableEvents => ScadaPermissionKeys.EditStructure,
            SystemAction.UIDesign => ScadaPermissionKeys.EditStructure,
            SystemAction.CameraSettings => ScadaPermissionKeys.HardwareConfig,
            SystemAction.CommSettings => ScadaPermissionKeys.HardwareConfig,
            SystemAction.HardwareSettings => ScadaPermissionKeys.HardwareConfig,
            SystemAction.IoSettings => ScadaPermissionKeys.HardwareConfig,
            SystemAction.MotionSettings => ScadaPermissionKeys.HardwareConfig,
            SystemAction.SystemSettings => ScadaPermissionKeys.SystemConfig,
            SystemAction.RuntimeWindowSettings => ScadaPermissionKeys.SystemConfig,
            SystemAction.SystemParameters => ScadaPermissionKeys.SystemConfig,
            SystemAction.UserManagement => ScadaPermissionKeys.UserAdministration,
            // 操作记录含登录失败与权限变更，与用户管理同权限收口
            SystemAction.OperationsLog => ScadaPermissionKeys.UserAdministration,
        };

        public static bool TryRequiredPermission(SystemAction action, out string key)
        {
            switch (action)
            {
                case SystemAction.UserLogin:
                case SystemAction.Help: key = ScadaPermissionKeys.Always; return true;
                case SystemAction.ReportQuery: key = ScadaPermissionKeys.ReportQuery; return true;
                case SystemAction.ReturnToZero:
                case SystemAction.MotionDebug:
                case SystemAction.MotionBoard:
                case SystemAction.AxisPoints: key = ScadaPermissionKeys.HardwareRuntime; return true;
                case SystemAction.GlobalVariables:
                case SystemAction.VariableEvents:
                case SystemAction.UIDesign: key = ScadaPermissionKeys.EditStructure; return true;
                case SystemAction.CameraSettings:
                case SystemAction.CommSettings:
                case SystemAction.HardwareSettings:
                case SystemAction.IoSettings:
                case SystemAction.MotionSettings: key = ScadaPermissionKeys.HardwareConfig; return true;
                case SystemAction.SystemSettings:
                case SystemAction.RuntimeWindowSettings:
                case SystemAction.SystemParameters: key = ScadaPermissionKeys.SystemConfig; return true;
                case SystemAction.UserManagement:
                case SystemAction.OperationsLog: key = ScadaPermissionKeys.UserAdministration; return true;
                default: key = string.Empty; return false;
            }
        }

        public static string RequiredPermission(FlowAction action) => action switch
        {
            FlowAction.Create => ScadaPermissionKeys.EditStructure,
            FlowAction.Delete => ScadaPermissionKeys.EditStructure,
            FlowAction.Rename => ScadaPermissionKeys.EditStructure,
            FlowAction.EditComment => ScadaPermissionKeys.EditStructure,
            FlowAction.ToggleEnabled => ScadaPermissionKeys.EditStructure,
            FlowAction.Edit => ScadaPermissionKeys.EditStructure,
            FlowAction.Manager => ScadaPermissionKeys.EditStructure,
            FlowAction.VariableEvents => ScadaPermissionKeys.EditStructure,
            FlowAction.BatchDisplayWindow => ScadaPermissionKeys.EditStructure,
            FlowAction.ResultSource => ScadaPermissionKeys.EditStructure,
            FlowAction.Compile => ScadaPermissionKeys.Compile,
            FlowAction.RunOnce => ScadaPermissionKeys.ControlRun,
            FlowAction.RunContinuous => ScadaPermissionKeys.ControlRun,
            FlowAction.Stop => ScadaPermissionKeys.Stop,
            FlowAction.BrowseList => ScadaPermissionKeys.Always,
        };

        public static bool TryRequiredPermission(FlowAction action, out string key)
        {
            switch (action)
            {
                case FlowAction.Create:
                case FlowAction.Delete:
                case FlowAction.Rename:
                case FlowAction.EditComment:
                case FlowAction.ToggleEnabled:
                case FlowAction.Edit:
                case FlowAction.Manager:
                case FlowAction.VariableEvents:
                case FlowAction.BatchDisplayWindow:
                case FlowAction.ResultSource: key = ScadaPermissionKeys.EditStructure; return true;
                case FlowAction.Compile: key = ScadaPermissionKeys.Compile; return true;
                case FlowAction.RunOnce:
                case FlowAction.RunContinuous: key = ScadaPermissionKeys.ControlRun; return true;
                case FlowAction.Stop: key = ScadaPermissionKeys.Stop; return true;
                case FlowAction.BrowseList: key = ScadaPermissionKeys.Always; return true;
                default: key = string.Empty; return false;
            }
        }

        public static string PermissionFor(SolutionAction action)
            => TryRequiredPermission(action, out var key) ? key : ScadaPermissionKeys.UserAdministration;

        public static string PermissionFor(ExecutionAction action)
            => TryRequiredPermission(action, out var key) ? key : ScadaPermissionKeys.UserAdministration;

        public static string PermissionFor(SystemAction action)
            => TryRequiredPermission(action, out var key) ? key : ScadaPermissionKeys.UserAdministration;

        public static string PermissionFor(FlowAction action)
            => TryRequiredPermission(action, out var key) ? key : ScadaPermissionKeys.UserAdministration;

        public static string Describe(SolutionAction action) => action switch
        {
            SolutionAction.Create => "新建方案",
            SolutionAction.Open => "打开方案",
            SolutionAction.BrowseList => "方案列表",
            SolutionAction.AlarmConfig => "报警配置",
            SolutionAction.Save => "保存方案",
            SolutionAction.SaveAs => "另存为",
            _ => action.ToString(),
        };

        public static string Describe(ExecutionAction action) => action switch
        {
            ExecutionAction.Compile => "编译",
            ExecutionAction.RunOnce => "单次运行",
            ExecutionAction.RunContinuous => "连续运行",
            ExecutionAction.Pause => "暂停",
            ExecutionAction.Resume => "继续",
            ExecutionAction.Stop => "停止",
            ExecutionAction.StepOnce => "单步",
            _ => action.ToString(),
        };

        public static string Describe(SystemAction action) => action switch
        {
            SystemAction.UserLogin => "用户登录",
            SystemAction.Help => "帮助",
            SystemAction.ReportQuery => "报表查询",
            SystemAction.ReturnToZero => "回零",
            SystemAction.MotionDebug => "运动调试",
            SystemAction.MotionBoard => "运动板卡",
            SystemAction.AxisPoints => "轴点位",
            SystemAction.GlobalVariables => "全局变量",
            SystemAction.VariableEvents => "变量事件",
            SystemAction.UIDesign => "界面设计",
            SystemAction.CameraSettings => "相机设置",
            SystemAction.CommSettings => "通讯设置",
            SystemAction.HardwareSettings => "硬件管理",
            SystemAction.IoSettings => "IO 设置",
            SystemAction.MotionSettings => "运动设置",
            SystemAction.SystemSettings => "系统设置",
            SystemAction.RuntimeWindowSettings => "运行窗口设置",
            SystemAction.SystemParameters => "系统参数",
            SystemAction.UserManagement => "用户管理",
            SystemAction.OperationsLog => "操作记录",
            _ => action.ToString(),
        };

        public static string Describe(FlowAction action) => action switch
        {
            FlowAction.Create => "新建流程",
            FlowAction.Delete => "删除流程",
            FlowAction.Rename => "重命名流程",
            FlowAction.EditComment => "编辑流程备注",
            FlowAction.ToggleEnabled => "启用/停用流程",
            FlowAction.Edit => "编辑流程",
            FlowAction.Manager => "流程管理",
            FlowAction.VariableEvents => "流程变量事件",
            FlowAction.BatchDisplayWindow => "批量设置显示窗口",
            FlowAction.ResultSource => "运行统计结果源",
            FlowAction.Compile => "编译流程",
            FlowAction.RunOnce => "单次运行流程",
            FlowAction.RunContinuous => "连续运行流程",
            FlowAction.Stop => "停止流程",
            FlowAction.BrowseList => "流程列表",
            _ => action.ToString(),
        };
    }

    /// <summary>一次拒绝的两件收尾事：说一句人话 + 落一条 Denied 台账。</summary>
    public static class ScadaPermissionDenial
    {
        public static string Message(string what, string? reason)
            => string.IsNullOrWhiteSpace(reason)
                ? $"没有权限执行「{what}」"
                : $"没有权限执行「{what}」：{reason}";

        public static void Audit(ScadaAuditWriter? audit, string? actor, string what, string message)
            => audit?.Append(new ScadaAuditEntry(
                   actor ?? "未登录", "SCADA", what, what, ScadaAuditOutcome.Denied, message));
    }
}
