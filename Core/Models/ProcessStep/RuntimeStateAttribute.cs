using System;

namespace VisionMaster.Models
{
    /// <summary>
    /// 标记「纯运行时状态」属性：其值变化不改变流程语义，因此不应递增 FlowModel.Version。
    ///
    /// 典型成员：执行状态 State、运行焦点 IsRunningFocus、耗时 LastRunTimeMs / CurrentRunTimeMs、
    /// 计时起点 LastRunStartTimestamp。
    ///
    /// 为什么用特性而不是在 FlowModel 里维护一份字符串名单：
    /// 旧实现写的是 "IsSelected" / "LastRunTime"，前者 StepModel 从未拥有，
    /// 后者在耗时 Stopwatch 改造后已更名为 LastRunTimeMs —— 名单静默失效，
    /// 于是每跑一轮流程 Version 就暴涨几十次，导致每次运行前都白白重编译一遍。
    /// 把「我是运行时属性」这个知识放在属性旁边，重命名或新增时不会漏改。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class RuntimeStateAttribute : Attribute
    {
    }
}
