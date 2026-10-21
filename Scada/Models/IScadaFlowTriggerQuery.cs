using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// "这个变量是不是流程触发器"的只读查询。
    ///
    /// <b>为什么 Web 命令层需要它</b>
    /// ---------
    /// A 范围（Web 轴只读）只排除直接的运动/流程启动动作还不够：Web 写一个被
    /// <c>FlowVariableTriggerService</c> 监视的变量，上升沿会启动流程，流程里可以含轴运动步骤。
    /// 这条路不经过 <c>ScadaActionType</c> 白名单，所以必须在写变量之前单独判一次。
    ///
    /// <b>为什么做成接口而不是让 Web 直接读工作区</b>
    /// ---------
    /// Web 模块只引用 <c>VM.Scada</c>，看不到 <c>IWorkspaceManager</c>（宿主层）。
    /// 实现放在宿主，Web 通过容器解析这个接口；解析不到时命令层按 <b>fail closed</b>
    /// 关闭写变量（宁可让现场看到"输入不可用"，也不能让一条 Web 写入间接启动流程）。
    ///
    /// <b>线程</b>：只在 UI 线程（命令泵）上调用；实现方可以安全遍历 <c>CurrentSolution.Flows</c>。
    /// </summary>
    public interface IScadaFlowTriggerQuery
    {
        /// <summary>
        /// 变量（Id 优先、名字兜底）是否是某个启用流程的变量触发器。
        /// </summary>
        /// <param name="variableId">变量稳定身份；<see cref="Guid.Empty"/> = 旧数据</param>
        /// <param name="variableName">变量名（旧数据兜底；实现方应用注册表解析出的当前名再比）</param>
        /// <param name="flowName">命中时给出流程名（可直接上屏/进审计）；未命中为 null</param>
        bool IsFlowTrigger(Guid variableId, string? variableName, out string? flowName);
    }
}
