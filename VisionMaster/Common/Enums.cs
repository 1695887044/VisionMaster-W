using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster
{
    public enum SessionState
    {
        Idle,
        Running,
        Paused,   // 🌟 暂停状态
        Stopped,
        Faulted
    }
    public enum BranchType
    {
        Default,
        If,    // 条件判断
        Else,   // 循环
        ElseIf,   // 循环
        Case, // 多分支
    }
    public enum SolutionAction
    {
        Create,            // 新建方案
        BrowseList,     // 方案列表
        Open,           // 打开方案
        Save            // 保存方案
    }
    public enum ExecutionAction
    {
        Compile,         // 编译
        RunOnce,         // 单次运行
        RunContinuous,   // 连续运行
        Stop             // 停止
    }

    /// <summary>
    /// 系统配置与全局管理相关指令
    /// </summary>
    public enum SystemAction
    {
        UserLogin,       // 用户登录
        GlobalVariables, // 全局变量
        CameraSettings,  // 相机设置
        CommSettings,    // 通讯设置
        HardwareSettings,// 硬件设置
        ReportQuery,     // 报表查询
        ReturnToZero,    // 回零
        UIDesign         // UI设计
    }
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
    public enum StepState
    {
        Idle,       // 默认/空闲 (灰色)
        Running,    // 正在执行 (黄色闪烁)
        Success,    // 执行成功 (绿色)
        Warning,    // 警告 (橙色)
        Error,       // 故障/报错 (红色)
        Cancel
    }
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
    public enum ModuleCommandAction
    {
        Rename,             // 重命名
        EditComment,        // 编辑注释
        ExecuteSelected,    // 执行选中模块
        ExecuteFromHere,    // 从此处执行
        ShowAll,            // 显示所有
        EnableSuperTool,    // 启用超级工具
        SetBreakpoint,      // 设置断点
        ModuleParameters,   // 模块参数
        Cut,                // 剪切
        Copy,               // 复制
        Paste,              // 粘贴
        Disable,            // 禁用
        Delete,              // 删除模块
        AddElseIf,  // 添加 ElseIf 分支
        AddElse,    // 添加 Else 分支
        AddCase     // 添加 Switch 的 Case 分支
    }
}
