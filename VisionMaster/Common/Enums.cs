using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster
{
    /// <summary>
    /// 会话状态枚举
    /// </summary>
    public enum SessionState
    {
        Idle,
        Running,
        Paused,
        Stopped,
        Faulted
    }

    /// <summary>
    /// 分支类型枚举
    /// </summary>
    public enum BranchType
    {
        Default,
        If,
        Else,
        ElseIf,
        Case,
    }

    /// <summary>
    /// 方案操作枚举
    /// </summary>
    public enum SolutionAction
    {
        Create,
        BrowseList,
        Open,
        Save
    }

    /// <summary>
    /// 执行操作枚举
    /// </summary>
    public enum ExecutionAction
    {
        Compile,
        RunOnce,
        RunContinuous,
        Stop
    }

    /// <summary>
    /// 系统操作枚举
    /// </summary>
    public enum SystemAction
    {
        UserLogin,
        GlobalVariables,
        CameraSettings,
        CommSettings,
        HardwareSettings,
        ReportQuery,
        ReturnToZero,
        UIDesign
    }

    /// <summary>
    /// 流程操作枚举
    /// </summary>
    public enum FlowAction
    {
        Create,
        Delete,
        BrowseList,
        Edit,
        Rename,
        EditComment,
        Encrypt,
        Decrypt
    }

    /// <summary>
    /// 步骤状态枚举
    /// </summary>
    public enum StepState
    {
        Idle,
        Running,
        Success,
        Warning,
        Error,
        Cancel
    }

    /// <summary>
    /// 流程触发模式枚举
    /// </summary>
    public enum FlowTriggerMode
    {
        [Display(Name = "单次执行", Description = "手动触发，流程只运行一次")]
        Single = 0,
        [Display(Name = "连续运行", Description = "无间断循环执行当前流程")]
        Continuous = 1,
        [Display(Name = "外部触发", Description = "等待外部 IO 或 PLC 信号触发执行")]
        External = 2,
        [Display(Name = "定时触发", Description = "按照设定的时间周期自动循环执行")]
        Timer = 3
    }

    /// <summary>
    /// 模块命令操作枚举
    /// </summary>
    public enum ModuleCommandAction
    {
        Rename,
        EditComment,
        ExecuteSelected,
        ExecuteFromHere,
        ShowAll,
        EnableSuperTool,
        SetBreakpoint,
        ModuleParameters,
        Cut,
        Copy,
        Paste,
        Disable,
        Delete,
        AddElseIf,
        AddElse,
        AddCase
    }
}
